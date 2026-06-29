using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class RhythmGameManager : MonoBehaviour
{
    public enum NoteType
    {
        Tap,
        SwipeLeftRight,
        SwipeDown,
        Windmill,
        Hold
    }

    public enum JudgmentResult
    {
        None,
        Perfect,
        Good,
        Miss
    }

    [Serializable]
    public struct NoteData
    {
        public float time;
        public NoteType type;
        public int lane;

        public NoteData(float time, NoteType type, int lane)
        {
            this.time = time;
            this.type = type;
            this.lane = lane;
        }
    }

    [Header("References")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private GameObject notePrefab;
    [SerializeField] private RectTransform noteParent;
    [SerializeField] private RectTransform judgmentLine;
    [SerializeField] private RectTransform perspectiveOrigin;
    [SerializeField] private RectTransform[] lanePoints = new RectTransform[5];

    [Header("Timing")]
    [SerializeField] private float spawnLeadTime = 2f;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private float perfectWindow = 0.08f;
    [SerializeField] private float goodWindow = 0.16f;
    [SerializeField] private float tapMaxDuration = 0.25f;
    [SerializeField] private float tapMaxMovement = 60f;
    [SerializeField] private float globalSongTimeOffsetSeconds = 0f;
    [SerializeField] private float iPhoneSongTimeOffsetSeconds = -0.045f;

    [Header("Lane Perspective")]
    [SerializeField, Range(0f, 0.4f)] private float spawnDepthOnLane = 0.04f;
    [SerializeField, Range(0.1f, 1f)] private float noteSpawnScale = 0.32f;
    [SerializeField, Range(0.5f, 2f)] private float noteHitScale = 1f;

    [Header("Performance")]
    [SerializeField] private int targetFrameRate = 60;
    [SerializeField] private bool disableVSync = true;

    [Header("Lane Visuals")]
    [SerializeField] private bool generateLaneVisuals = true;
    [SerializeField] private float laneVisualWidth = 140f;
    [SerializeField] private float laneGlowWidth = 24f;
    [SerializeField] private float laneVisualInsetFromHit = 18f;
    [SerializeField] private Color laneBaseColor = new Color(0.03f, 0.03f, 0.05f, 0.82f);
    [SerializeField] private Color laneEdgeColor = new Color(1f, 1f, 1f, 0.14f);

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI comboText;
    [SerializeField] private TextMeshProUGUI judgmentText;
    [SerializeField] private Color judgmentLineColor = new Color(1f, 1f, 1f, 0.85f);
    [SerializeField] private Vector2 judgmentLineSize = new Vector2(920f, 10f);

    [Header("Touch Areas")]
    [SerializeField] private RectTransform[] laneTouchAreas = new RectTransform[5];
    [SerializeField] private Vector2 laneTouchAreaSize = new Vector2(160f, 170f);
    [SerializeField] private float laneTouchAreaYOffset = -170f;
    [SerializeField] private Color laneTouchAreaColor = new Color(1f, 1f, 1f, 0.08f);
    [SerializeField] private Color laneTouchAreaBorderColor = new Color(1f, 1f, 1f, 0.22f);

    [Header("Editor Guides")]
    [SerializeField] private bool showLanePointGizmos = true;
    [SerializeField] private float lanePointGizmoRadius = 28f;

    [Header("Lane Alignment")]
    [SerializeField] private bool autoAlignLanePointsToJudgmentLine = true;

    [Header("Chart Source")]
    [SerializeField] private bool loadChartFromJson = true;
    [SerializeField] private string chartFileName = "Racing";
    [SerializeField] private TextAsset chartJsonAsset;

    [Header("Prototype Chart")]
    [SerializeField] private NoteData[] chart =
    {
        new NoteData(1.5f, NoteType.Tap, 0),
        new NoteData(2.0f, NoteType.Tap, 1),
        new NoteData(2.0f, NoteType.Tap, 2),
        new NoteData(2.5f, NoteType.Tap, 3),
        new NoteData(3.0f, NoteType.Tap, 0),
        new NoteData(3.5f, NoteType.Tap, 1),
        new NoteData(4.0f, NoteType.Hold, 4),
        new NoteData(4.5f, NoteType.Tap, 2),
        new NoteData(5.0f, NoteType.Tap, 3),
        new NoteData(5.5f, NoteType.Tap, 0)
    };

    private int nextNoteIndex;
    private int score;
    private int combo;
    private float judgmentDisplayTimer;
    private bool hasLoggedSetupError;
    private bool gestureInProgress;
    private Vector2 gestureStartPosition;
    private float gestureStartTime;
    private List<RhythmNote>[] activeNotesByLane;

    public float GoodWindow => goodWindow;

    private void Awake()
    {
        ApplyPerformanceSettings();

        activeNotesByLane = new List<RhythmNote>[5];
        for (int i = 0; i < activeNotesByLane.Length; i++)
        {
            activeNotesByLane[i] = new List<RhythmNote>();
        }

        ResolveReferences();
        LoadChartIfAvailable();

        if (chart != null)
        {
            Array.Sort(chart, (a, b) => a.time.CompareTo(b.time));
        }
    }

    public bool ReloadChartFromConfiguredSource(bool restartPlayback)
    {
        bool loaded = LoadChartIfAvailable();
        if (loaded && restartPlayback)
        {
            RestartChartPlayback(true);
        }

        return loaded;
    }

    private void Start()
    {
        ApplyPerformanceSettings();
        ResolveReferences();
        EnsureJudgmentLineVisual();
        SyncLanePointsToJudgmentLine();
        EnsureLaneVisuals();
        EnsureGameplayUi();
        EnsureLaneTouchAreas();
        RefreshScoreUi();
        RefreshComboUi();
        HideJudgmentText();

        if (playOnStart && musicSource != null)
        {
            musicSource.Play();
        }
    }

    private void Update()
    {
        if (!CanSpawnNotes())
        {
            return;
        }

        if (SyncLanePointsToJudgmentLine())
        {
            EnsureLaneVisuals();
            EnsureLaneTouchAreas();
        }

        HandleGestureInput();
        UpdateJudgmentText();

        float songTime = GetSongTime();

        while (nextNoteIndex < chart.Length && songTime >= chart[nextNoteIndex].time - spawnLeadTime)
        {
            SpawnNote(chart[nextNoteIndex]);
            nextNoteIndex++;
        }
    }

    private void OnValidate()
    {
        ApplyPerformanceSettings();
        ResolveReferences();
        if (SyncLanePointsToJudgmentLine())
        {
            EnsureLaneVisuals();
            EnsureLaneTouchAreas();
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!showLanePointGizmos || lanePoints == null)
        {
            return;
        }

        for (int i = 0; i < lanePoints.Length; i++)
        {
            RectTransform lanePoint = lanePoints[i];
            if (lanePoint == null)
            {
                continue;
            }

            Vector3 worldPosition = lanePoint.position;
            Color laneColor = GetLaneColor(i, 0.95f);
            laneColor.a = 0.95f;

            Handles.color = laneColor;
            Handles.DrawSolidDisc(worldPosition, Vector3.forward, lanePointGizmoRadius);

            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(worldPosition, lanePointGizmoRadius);

            Handles.Label(
                worldPosition + new Vector3(0f, lanePointGizmoRadius + 18f, 0f),
                $"Lane{i}",
                CreateLaneGuideLabelStyle(laneColor));
        }
    }

    private GUIStyle CreateLaneGuideLabelStyle(Color laneColor)
    {
        GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
        style.alignment = TextAnchor.MiddleCenter;
        style.normal.textColor = laneColor;
        return style;
    }
#endif

    private bool CanSpawnNotes()
    {
        ResolveReferences();

        if (musicSource == null || notePrefab == null || noteParent == null || judgmentLine == null)
        {
            LogMissingReferences();
            return false;
        }

        if (lanePoints == null || lanePoints.Length < 5)
        {
            LogMissingReferences();
            return false;
        }

        for (int i = 0; i < lanePoints.Length; i++)
        {
            if (lanePoints[i] == null)
            {
                LogMissingReferences();
                return false;
            }
        }

        return chart != null && chart.Length > 0;
    }

    public float GetSongTime()
    {
        if (musicSource == null)
        {
            return 0f;
        }

        AudioClip clip = musicSource.clip;
        if (clip != null && clip.frequency > 0 && musicSource.timeSamples >= 0)
        {
            float rawSongTime = (float)musicSource.timeSamples / clip.frequency;
            return Mathf.Max(0f, rawSongTime + GetPlatformSongTimeOffset());
        }

        return 0f;
    }

    private float GetPlatformSongTimeOffset()
    {
        float offset = globalSongTimeOffsetSeconds;

        if (Application.platform == RuntimePlatform.IPhonePlayer)
        {
            offset += iPhoneSongTimeOffsetSeconds;
        }

        return offset;
    }

    public void RestartChartPlayback(bool restartMusic)
    {
        nextNoteIndex = 0;
        score = 0;
        combo = 0;
        judgmentDisplayTimer = 0f;
        gestureInProgress = false;

        ClearActiveNotes();
        RefreshScoreUi();
        RefreshComboUi();
        HideJudgmentText();

        if (musicSource == null)
        {
            return;
        }

        if (restartMusic)
        {
            musicSource.Stop();
            musicSource.timeSamples = 0;
            musicSource.Play();
        }
    }

    [ContextMenu("Auto Assign References")]
    private void ResolveReferences()
    {
        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }

        if (musicSource == null)
        {
            musicSource = GetComponentInChildren<AudioSource>();
        }

        if (noteParent == null)
        {
            noteParent = FindRectTransformInScene("LaneArea");
        }

        if (perspectiveOrigin == null)
        {
            perspectiveOrigin = FindRectTransformInScene("PerspectiveOrigin");
        }

        if (perspectiveOrigin == null)
        {
            perspectiveOrigin = FindRectTransformInScene("LaneOrigin");
        }

        if (judgmentLine == null)
        {
            judgmentLine = FindRectTransformInScene("JudgementLine");
        }

        if (judgmentLine == null)
        {
            judgmentLine = FindRectTransformInScene("JudgmentLine");
        }

        if (lanePoints == null || lanePoints.Length != 5)
        {
            lanePoints = new RectTransform[5];
        }

        for (int i = 0; i < lanePoints.Length; i++)
        {
            if (lanePoints[i] != null)
            {
                continue;
            }

            string laneName = $"Lane{i}";

            if (noteParent != null)
            {
                Transform laneTransform = noteParent.Find(laneName);
                if (laneTransform != null)
                {
                    lanePoints[i] = laneTransform as RectTransform;
                    continue;
                }
            }

            lanePoints[i] = FindRectTransformInScene(laneName);
        }
    }

    private void LogMissingReferences()
    {
        if (hasLoggedSetupError)
        {
            return;
        }

        hasLoggedSetupError = true;

        Debug.LogWarning(
            $"RhythmGameManager setup is incomplete. " +
            $"musicSource={(musicSource != null)}, " +
            $"notePrefab={(notePrefab != null)}, " +
            $"noteParent={(noteParent != null ? noteParent.name : "null")}, " +
            $"judgmentLine={(judgmentLine != null ? judgmentLine.name : "null")}, " +
            $"perspectiveOrigin={(perspectiveOrigin != null ? perspectiveOrigin.name : "null")}, " +
            $"lanePoints={GetLaneStatus()}");
    }

    private string GetLaneStatus()
    {
        if (lanePoints == null)
        {
            return "null";
        }

        string[] names = new string[lanePoints.Length];

        for (int i = 0; i < lanePoints.Length; i++)
        {
            names[i] = lanePoints[i] != null ? lanePoints[i].name : $"Lane{i}:null";
        }

        return string.Join(", ", names);
    }

    private RectTransform FindRectTransformInScene(string objectName)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] rootObjects = activeScene.GetRootGameObjects();

        for (int i = 0; i < rootObjects.Length; i++)
        {
            Transform found = FindChildRecursive(rootObjects[i].transform, objectName);
            if (found != null)
            {
                return found as RectTransform;
            }
        }

        return null;
    }

    private bool SyncLanePointsToJudgmentLine()
    {
        if (!autoAlignLanePointsToJudgmentLine || judgmentLine == null || lanePoints == null)
        {
            return false;
        }

        bool changed = false;
        float targetY = judgmentLine.anchoredPosition.y;

        for (int i = 0; i < lanePoints.Length; i++)
        {
            RectTransform lanePoint = lanePoints[i];
            if (lanePoint == null)
            {
                continue;
            }

            Vector2 anchoredPosition = lanePoint.anchoredPosition;
            if (Mathf.Approximately(anchoredPosition.y, targetY))
            {
                continue;
            }

            lanePoint.anchoredPosition = new Vector2(anchoredPosition.x, targetY);
            changed = true;
        }

        return changed;
    }

    private Transform FindChildRecursive(Transform parent, string objectName)
    {
        if (parent.name == objectName)
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildRecursive(parent.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void SpawnNote(NoteData noteData)
    {
        if (noteData.lane < 0 || noteData.lane >= lanePoints.Length || lanePoints[noteData.lane] == null)
        {
            Debug.LogWarning($"Lane {noteData.lane} is not assigned.");
            return;
        }

        noteData.type = NormalizeNoteTypeForLane(noteData.lane);

        GameObject noteObject = Instantiate(notePrefab, noteParent);
        PlaceNoteBehindTouchOverlays(noteObject.transform);
        RhythmNote rhythmNote = noteObject.GetComponent<RhythmNote>();

        if (rhythmNote == null)
        {
            rhythmNote = noteObject.AddComponent<RhythmNote>();
        }

        Vector2 targetPosition = lanePoints[noteData.lane].anchoredPosition;
        Vector2 spawnPosition = GetSpawnPosition(targetPosition);

        rhythmNote.Initialize(
            this,
            musicSource,
            noteData.time,
            spawnLeadTime,
            spawnPosition,
            targetPosition,
            noteSpawnScale,
            noteHitScale,
            noteData.type,
            noteData.lane);

        activeNotesByLane[noteData.lane].Add(rhythmNote);
    }

    private void PlaceNoteBehindTouchOverlays(Transform noteTransform)
    {
        if (noteTransform == null)
        {
            return;
        }

        Transform touchAreasRoot = noteParent != null ? noteParent.Find("LaneTouchAreas") : null;
        if (touchAreasRoot != null)
        {
            noteTransform.SetSiblingIndex(touchAreasRoot.GetSiblingIndex());
        }

        if (judgmentLine != null)
        {
            judgmentLine.SetAsLastSibling();
        }
    }

    private void HandleGestureInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Touchscreen.current != null)
        {
            var primaryTouch = Touchscreen.current.primaryTouch;
            if (primaryTouch.press.wasPressedThisFrame)
            {
                BeginGesture(primaryTouch.position.ReadValue());
            }

            if (primaryTouch.press.wasReleasedThisFrame)
            {
                EndGesture(primaryTouch.position.ReadValue());
            }
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            BeginGesture(Mouse.current.position.ReadValue());
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            EndGesture(Mouse.current.position.ReadValue());
        }
#else
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    BeginGesture(touch.position);
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    EndGesture(touch.position);
                }
            }
        }

        if (Input.GetMouseButtonDown(0))
        {
            BeginGesture(Input.mousePosition);
        }

        if (Input.GetMouseButtonUp(0))
        {
            EndGesture(Input.mousePosition);
        }
#endif
    }

    private void BeginGesture(Vector2 screenPosition)
    {
        gestureInProgress = true;
        gestureStartPosition = screenPosition;
        gestureStartTime = Time.unscaledTime;
    }

    private void EndGesture(Vector2 screenPosition)
    {
        if (!gestureInProgress)
        {
            return;
        }

        gestureInProgress = false;

        Vector2 delta = screenPosition - gestureStartPosition;
        float duration = Time.unscaledTime - gestureStartTime;

        if (duration <= tapMaxDuration && delta.magnitude <= tapMaxMovement)
        {
            int tappedLane = GetTappedLaneIndex(screenPosition);
            if (tappedLane >= 0)
            {
                TryHitLane(tappedLane);
            }
        }
    }

    private bool TryHitLane(int laneIndex)
    {
        if (laneIndex < 0 || laneIndex >= activeNotesByLane.Length)
        {
            return false;
        }

        if (laneIndex == 4)
        {
            return false;
        }

        RhythmNote candidate = GetBestCandidateInLane(laneIndex);
        if (candidate == null)
        {
            return false;
        }

        float timeOffset = Mathf.Abs(GetSongTime() - candidate.HitTime);
        if (timeOffset > goodWindow)
        {
            return false;
        }

        JudgmentResult result = timeOffset <= perfectWindow ? JudgmentResult.Perfect : JudgmentResult.Good;
        RegisterJudgment(result, candidate.Lane);
        Debug.Log($"{result}! {candidate.NoteType} Lane {candidate.Lane}");
        candidate.Hit();
        return true;
    }

    private RhythmNote GetBestCandidateInLane(int laneIndex)
    {
        RhythmNote bestCandidate = null;
        float bestTimeOffset = float.MaxValue;
        List<RhythmNote> laneNotes = activeNotesByLane[laneIndex];

        for (int j = 0; j < laneNotes.Count; j++)
        {
            RhythmNote note = laneNotes[j];
            if (note == null)
            {
                laneNotes.RemoveAt(j);
                j--;
                continue;
            }

            if (!IsTapLaneNote(note))
            {
                continue;
            }

            float timeOffset = Mathf.Abs(GetSongTime() - note.HitTime);
            if (timeOffset < bestTimeOffset)
            {
                bestTimeOffset = timeOffset;
                bestCandidate = note;
            }
        }

        return bestCandidate;
    }

    public void NotifyNoteResolved(RhythmNote note, bool wasHit)
    {
        if (note == null || note.Lane < 0 || note.Lane >= activeNotesByLane.Length)
        {
            return;
        }

        activeNotesByLane[note.Lane].Remove(note);

        if (!wasHit && IsJudgeableNote(note))
        {
            RegisterJudgment(JudgmentResult.Miss, note.Lane);
            Debug.Log($"Miss... {note.NoteType} Lane {note.Lane}");
        }
    }

    private bool IsJudgeableNote(RhythmNote note)
    {
        return note != null && IsTapLaneNote(note);
    }

    private bool IsTapLaneNote(RhythmNote note)
    {
        return note != null && note.Lane >= 0 && note.Lane <= 3 && note.NoteType != NoteType.Hold;
    }

    private void RegisterJudgment(JudgmentResult result, int lane)
    {
        switch (result)
        {
            case JudgmentResult.Perfect:
                score += 1000;
                combo++;
                break;
            case JudgmentResult.Good:
                score += 500;
                combo++;
                break;
            case JudgmentResult.Miss:
                combo = 0;
                break;
        }

        RefreshScoreUi();
        RefreshComboUi();
        ShowJudgment(result);
    }

    private void RefreshScoreUi()
    {
        if (scoreText != null)
        {
            scoreText.text = $"SCORE\n{score:N0}";
        }
    }

    private void RefreshComboUi()
    {
        if (comboText != null)
        {
            comboText.text = combo > 0 ? $"COMBO\n{combo}" : "COMBO\n0";
        }
    }

    private void ShowJudgment(JudgmentResult result)
    {
        if (judgmentText == null)
        {
            return;
        }

        judgmentDisplayTimer = 0.6f;
        judgmentText.gameObject.SetActive(true);

        switch (result)
        {
            case JudgmentResult.Perfect:
                judgmentText.text = "PERFECT!";
                judgmentText.color = new Color(1f, 0.9f, 0.2f, 1f);
                break;
            case JudgmentResult.Good:
                judgmentText.text = "GOOD!";
                judgmentText.color = new Color(0.3f, 0.9f, 1f, 1f);
                break;
            case JudgmentResult.Miss:
                judgmentText.text = "MISS...";
                judgmentText.color = new Color(1f, 0.35f, 0.35f, 1f);
                break;
            default:
                judgmentText.text = string.Empty;
                break;
        }
    }

    private void UpdateJudgmentText()
    {
        if (judgmentText == null || !judgmentText.gameObject.activeSelf)
        {
            return;
        }

        if (judgmentDisplayTimer > 0f)
        {
            judgmentDisplayTimer -= Time.deltaTime;
            Color color = judgmentText.color;
            color.a = Mathf.Clamp01(judgmentDisplayTimer / 0.6f);
            judgmentText.color = color;
            return;
        }

        HideJudgmentText();
    }

    private void HideJudgmentText()
    {
        if (judgmentText == null)
        {
            return;
        }

        judgmentText.text = string.Empty;
        judgmentText.gameObject.SetActive(false);
    }

    private void EnsureGameplayUi()
    {
        Canvas canvas = noteParent != null ? noteParent.root.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            return;
        }

        Transform uiRoot = canvas.transform.Find("PrototypeHUD");
        if (uiRoot == null)
        {
            GameObject uiRootObject = new GameObject("PrototypeHUD", typeof(RectTransform));
            RectTransform uiRootRect = uiRootObject.GetComponent<RectTransform>();
            uiRootRect.SetParent(canvas.transform, false);
            uiRootRect.anchorMin = Vector2.zero;
            uiRootRect.anchorMax = Vector2.one;
            uiRootRect.offsetMin = Vector2.zero;
            uiRootRect.offsetMax = Vector2.zero;
            uiRoot = uiRootRect;
        }

        if (scoreText == null)
        {
            scoreText = CreateHudText(
                uiRoot,
                "ScoreText",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -24f),
                TextAlignmentOptions.TopLeft,
                42,
                FontStyles.Bold);
        }

        if (comboText == null)
        {
            comboText = CreateHudText(
                uiRoot,
                "ComboText",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 140f),
                TextAlignmentOptions.Center,
                48,
                FontStyles.Bold);
        }

        if (judgmentText == null)
        {
            judgmentText = CreateHudText(
                uiRoot,
                "JudgmentText",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 240f),
                TextAlignmentOptions.Center,
                56,
                FontStyles.Bold);
        }
    }

    private void EnsureLaneTouchAreas()
    {
        if (noteParent == null || lanePoints == null || lanePoints.Length == 0)
        {
            return;
        }

        if (laneTouchAreas == null || laneTouchAreas.Length != lanePoints.Length)
        {
            laneTouchAreas = new RectTransform[lanePoints.Length];
        }

        Transform areasRoot = noteParent.Find("LaneTouchAreas");
        if (areasRoot == null)
        {
            return;
        }

        areasRoot.SetAsLastSibling();

        for (int i = 0; i < lanePoints.Length; i++)
        {
            if (lanePoints[i] == null)
            {
                continue;
            }

            laneTouchAreas[i] = EnsureLaneTouchArea(areasRoot, i);
        }

        if (judgmentLine != null)
        {
            judgmentLine.SetAsLastSibling();
        }
    }

    private RectTransform EnsureLaneTouchArea(Transform parent, int laneIndex)
    {
        string areaObjectName = $"LaneTouchArea_{laneIndex}";
        Transform existing = parent.Find(areaObjectName);
        if (existing == null)
        {
            return null;
        }

        RectTransform areaRect = existing as RectTransform;
        Image areaImage = existing.GetComponent<Image>();
        if (areaRect == null || areaImage == null)
        {
            return null;
        }

        areaRect.anchorMin = new Vector2(0.5f, 0.5f);
        areaRect.anchorMax = new Vector2(0.5f, 0.5f);
        areaRect.pivot = new Vector2(0.5f, 0.5f);
        areaRect.anchoredPosition = lanePoints[laneIndex].anchoredPosition + new Vector2(0f, laneTouchAreaYOffset);
        areaRect.sizeDelta = laneTouchAreaSize;
        areaRect.localRotation = Quaternion.identity;
        areaRect.localScale = Vector3.one;

        areaImage.color = laneIndex == 4
            ? new Color(1f, 0.52f, 0.12f, laneTouchAreaColor.a + 0.05f)
            : GetLaneColor(laneIndex, laneTouchAreaColor.a);
        areaImage.raycastTarget = false;

        EnsureLaneTouchAreaBorder(areaRect, laneIndex);
        EnsureLaneTouchAreaLabel(areaRect, laneIndex);
        return areaRect;
    }

    private void EnsureLaneTouchAreaBorder(RectTransform parent, int laneIndex)
    {
        Transform existing = parent.Find("Border");
        RectTransform borderRect;
        Image borderImage;

        if (existing == null)
        {
            GameObject borderObject = new GameObject("Border", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            borderRect = borderObject.GetComponent<RectTransform>();
            borderRect.SetParent(parent, false);
            borderImage = borderObject.GetComponent<Image>();
        }
        else
        {
            borderRect = existing as RectTransform;
            borderImage = existing.GetComponent<Image>();
        }

        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.offsetMin = Vector2.zero;
        borderRect.offsetMax = Vector2.zero;
        borderImage.color = laneIndex == 4
            ? new Color(1f, 0.72f, 0.28f, laneTouchAreaBorderColor.a)
            : GetLaneColor(laneIndex, laneTouchAreaBorderColor.a);
        borderImage.raycastTarget = false;

        Transform innerExisting = borderRect.Find("Inner");
        RectTransform innerRect;
        Image innerImage;

        if (innerExisting == null)
        {
            GameObject innerObject = new GameObject("Inner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            innerRect = innerObject.GetComponent<RectTransform>();
            innerRect.SetParent(borderRect, false);
            innerImage = innerObject.GetComponent<Image>();
        }
        else
        {
            innerRect = innerExisting as RectTransform;
            innerImage = innerExisting.GetComponent<Image>();
        }

        innerRect.anchorMin = Vector2.zero;
        innerRect.anchorMax = Vector2.one;
        innerRect.offsetMin = new Vector2(5f, 5f);
        innerRect.offsetMax = new Vector2(-5f, -5f);
        innerImage.color = laneIndex == 4
            ? new Color(1f, 0.52f, 0.12f, laneTouchAreaColor.a + 0.05f)
            : GetLaneColor(laneIndex, laneTouchAreaColor.a);
        innerImage.raycastTarget = false;
    }

    private void EnsureLaneTouchAreaLabel(RectTransform parent, int laneIndex)
    {
        string labelName = laneIndex == 4 ? "HOLD" : "TAP";
        Transform existing = parent.Find("Label");
        TextMeshProUGUI label = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
        if (label == null)
        {
            label = CreateHudText(
                parent,
                "Label",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                TextAlignmentOptions.Center,
                28,
                FontStyles.Bold);
        }

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 8f);
        labelRect.offsetMax = new Vector2(-8f, -8f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.pivot = new Vector2(0.5f, 0.5f);

        label.text = labelName;
        label.color = laneIndex == 4
            ? new Color(1f, 0.88f, 0.58f, 0.95f)
            : GetLaneColor(laneIndex, 0.95f);
    }

    private void EnsureJudgmentLineVisual()
    {
        if (judgmentLine == null)
        {
            judgmentLine = CreateJudgmentLineIfMissing();
        }

        if (judgmentLine == null)
        {
            return;
        }

        Image lineImage = judgmentLine.GetComponent<Image>();
        if (lineImage == null)
        {
            lineImage = judgmentLine.gameObject.AddComponent<Image>();
        }

        lineImage.color = judgmentLineColor;
        lineImage.raycastTarget = false;

        judgmentLine.anchorMin = new Vector2(0.5f, 0.5f);
        judgmentLine.anchorMax = new Vector2(0.5f, 0.5f);
        judgmentLine.pivot = new Vector2(0.5f, 0.5f);
        judgmentLine.sizeDelta = judgmentLineSize;
        judgmentLine.SetAsLastSibling();
    }

    private RectTransform CreateJudgmentLineIfMissing()
    {
        if (noteParent == null)
        {
            return null;
        }

        GameObject lineObject = new GameObject("JudgementLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform lineRect = lineObject.GetComponent<RectTransform>();
        lineRect.SetParent(noteParent, false);
        lineRect.anchoredPosition = new Vector2(0f, -460f);
        return lineRect;
    }

    private void EnsureLaneVisuals()
    {
        if (!generateLaneVisuals || noteParent == null || perspectiveOrigin == null || lanePoints == null || lanePoints.Length == 0)
        {
            return;
        }

        Transform visualsRoot = noteParent.Find("LaneVisuals");
        if (visualsRoot == null)
        {
            GameObject rootObject = new GameObject("LaneVisuals", typeof(RectTransform));
            RectTransform rootRect = rootObject.GetComponent<RectTransform>();
            rootRect.SetParent(noteParent, false);
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = noteParent.rect.size;
            visualsRoot = rootRect;
        }

        visualsRoot.SetSiblingIndex(0);

        for (int i = 0; i < lanePoints.Length; i++)
        {
            if (lanePoints[i] == null)
            {
                continue;
            }

            EnsureLaneVisual(visualsRoot, i);
        }
    }

    private void EnsureLaneVisual(Transform parent, int laneIndex)
    {
        string laneObjectName = $"LaneVisual_{laneIndex}";
        Transform laneTransform = parent.Find(laneObjectName);
        RectTransform laneRect;
        Image laneImage;

        if (laneTransform == null)
        {
            GameObject laneObject = new GameObject(laneObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            laneRect = laneObject.GetComponent<RectTransform>();
            laneRect.SetParent(parent, false);
            laneImage = laneObject.GetComponent<Image>();
        }
        else
        {
            laneRect = laneTransform as RectTransform;
            laneImage = laneTransform.GetComponent<Image>();
        }

        ConfigureLaneRect(laneRect, lanePoints[laneIndex].anchoredPosition, laneVisualWidth, laneVisualInsetFromHit);
        laneImage.color = laneBaseColor;
        laneImage.raycastTarget = false;

        string edgeObjectName = "Edge";
        Transform edgeTransform = laneRect.Find(edgeObjectName);
        RectTransform edgeRect;
        Image edgeImage;

        if (edgeTransform == null)
        {
            GameObject edgeObject = new GameObject(edgeObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            edgeRect = edgeObject.GetComponent<RectTransform>();
            edgeRect.SetParent(laneRect, false);
            edgeImage = edgeObject.GetComponent<Image>();
        }
        else
        {
            edgeRect = edgeTransform as RectTransform;
            edgeImage = edgeTransform.GetComponent<Image>();
        }

        edgeRect.anchorMin = new Vector2(0f, 0.5f);
        edgeRect.anchorMax = new Vector2(1f, 0.5f);
        edgeRect.pivot = new Vector2(0.5f, 0.5f);
        edgeRect.anchoredPosition = Vector2.zero;
        edgeRect.sizeDelta = new Vector2(0f, laneVisualWidth);
        edgeImage.color = laneEdgeColor;
        edgeImage.raycastTarget = false;

        string glowObjectName = "Glow";
        Transform glowTransform = laneRect.Find(glowObjectName);
        RectTransform glowRect;
        Image glowImage;

        if (glowTransform == null)
        {
            GameObject glowObject = new GameObject(glowObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            glowRect = glowObject.GetComponent<RectTransform>();
            glowRect.SetParent(laneRect, false);
            glowImage = glowObject.GetComponent<Image>();
        }
        else
        {
            glowRect = glowTransform as RectTransform;
            glowImage = glowTransform.GetComponent<Image>();
        }

        glowRect.anchorMin = new Vector2(0.5f, 0.5f);
        glowRect.anchorMax = new Vector2(0.5f, 0.5f);
        glowRect.pivot = new Vector2(0.5f, 0.5f);
        glowRect.anchoredPosition = Vector2.zero;
        glowRect.sizeDelta = new Vector2(laneRect.sizeDelta.x, laneGlowWidth);
        glowImage.color = GetLaneColor(laneIndex, 0.82f);
        glowImage.raycastTarget = false;
    }

    private void ConfigureLaneRect(RectTransform laneRect, Vector2 targetPosition, float width, float hitInset)
    {
        Vector2 originPosition = perspectiveOrigin.anchoredPosition;
        Vector2 direction = targetPosition - originPosition;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
        {
            return;
        }

        Vector2 endPosition = targetPosition - direction.normalized * hitInset;
        Vector2 laneVector = endPosition - originPosition;
        float laneLength = laneVector.magnitude;
        float angle = Mathf.Atan2(laneVector.y, laneVector.x) * Mathf.Rad2Deg;

        laneRect.anchorMin = new Vector2(0.5f, 0.5f);
        laneRect.anchorMax = new Vector2(0.5f, 0.5f);
        laneRect.pivot = new Vector2(0.5f, 0.5f);
        laneRect.anchoredPosition = (originPosition + endPosition) * 0.5f;
        laneRect.sizeDelta = new Vector2(laneLength, width);
        laneRect.localRotation = Quaternion.Euler(0f, 0f, angle);
        laneRect.localScale = Vector3.one;
    }

    private Color GetLaneColor(int laneIndex, float alpha)
    {
        Color color;
        switch (laneIndex)
        {
            case 0:
                color = new Color(0.69f, 0.27f, 1f, alpha);
                break;
            case 1:
                color = new Color(0.20f, 0.62f, 1f, alpha);
                break;
            case 2:
                color = new Color(1f, 0.25f, 0.22f, alpha);
                break;
            case 3:
                color = new Color(0.32f, 1f, 0.22f, alpha);
                break;
            case 4:
                color = new Color(1f, 0.60f, 0.12f, alpha);
                break;
            default:
                color = new Color(1f, 1f, 1f, alpha);
                break;
        }

        color.a = alpha;
        return color;
    }

    private NoteType NormalizeNoteTypeForLane(int lane)
    {
        return lane == 4 ? NoteType.Hold : NoteType.Tap;
    }

    private int GetTappedLaneIndex(Vector2 screenPosition)
    {
        Canvas canvas = noteParent != null ? noteParent.root.GetComponent<Canvas>() : FindFirstObjectByType<Canvas>();
        Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        if (laneTouchAreas == null)
        {
            return -1;
        }

        for (int i = 0; i < laneTouchAreas.Length; i++)
        {
            RectTransform area = laneTouchAreas[i];
            if (area == null)
            {
                continue;
            }

            if (RectTransformUtility.RectangleContainsScreenPoint(area, screenPosition, eventCamera))
            {
                return i;
            }
        }

        return -1;
    }

    private TextMeshProUGUI CreateHudText(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        TextAlignmentOptions alignment,
        int fontSize,
        FontStyles fontStyle)
    {
        Transform existing = parent.Find(objectName);
        TextMeshProUGUI text = existing != null ? existing.GetComponent<TextMeshProUGUI>() : null;
        if (text != null)
        {
            return text;
        }

        GameObject textObject = existing != null
            ? existing.gameObject
            : new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.SetParent(parent, false);
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = new Vector2(420f, 120f);

        text = textObject.GetComponent<TextMeshProUGUI>();
        if (text == null)
        {
            text = textObject.AddComponent<TextMeshProUGUI>();
        }

        TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
        if (defaultFont == null)
        {
            defaultFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        }

        if (defaultFont != null)
        {
            text.font = defaultFont;
        }

        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = alignment;
        text.color = Color.white;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.text = string.Empty;

        return text;
    }

    private Vector2 GetSpawnPosition(Vector2 targetPosition)
    {
        if (perspectiveOrigin == null)
        {
            return new Vector2(targetPosition.x, targetPosition.y + 900f);
        }

        return Vector2.Lerp(perspectiveOrigin.anchoredPosition, targetPosition, spawnDepthOnLane);
    }

    private bool LoadChartIfAvailable()
    {
        if (!loadChartFromJson)
        {
            return false;
        }

        string json = TryLoadChartJson();
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        ChartFileData loadedChart = JsonUtility.FromJson<ChartFileData>(json);
        if (loadedChart == null || loadedChart.notes == null || loadedChart.notes.Length == 0)
        {
            Debug.LogWarning("Chart JSON was found, but it did not contain any notes.");
            return false;
        }

        NoteData[] loadedNotes = new NoteData[loadedChart.notes.Length];
        for (int i = 0; i < loadedChart.notes.Length; i++)
        {
            ChartNoteData note = loadedChart.notes[i];
            loadedNotes[i] = new NoteData(
                note.time,
                NormalizeNoteTypeForLane(note.lane),
                note.lane);
        }

        chart = loadedNotes;
        Debug.Log($"Loaded chart '{loadedChart.songName}' with {chart.Length} notes.");
        return true;
    }

    private string TryLoadChartJson()
    {
#if UNITY_EDITOR
        if (chartJsonAsset != null)
        {
            Debug.Log($"Loaded chart JSON from assigned TextAsset: {chartJsonAsset.name}");
            return chartJsonAsset.text;
        }

        TextAsset resourceChartInEditor = Resources.Load<TextAsset>($"Charts/{chartFileName}");
        if (resourceChartInEditor != null)
        {
            Debug.Log($"Loaded chart JSON from Resources/Charts/{chartFileName}");
            return resourceChartInEditor.text;
        }

        string editorAssetChartPath = Path.Combine(Application.dataPath, "04.Charts", $"{chartFileName}.json");
        if (File.Exists(editorAssetChartPath))
        {
            Debug.Log($"Loaded chart JSON from editor asset path: {editorAssetChartPath}");
            return File.ReadAllText(editorAssetChartPath);
        }
#endif

        string persistentPath = Path.Combine(Application.persistentDataPath, $"{chartFileName}.json");
        if (File.Exists(persistentPath))
        {
            Debug.Log($"Loaded chart JSON from persistent data: {persistentPath}");
            return File.ReadAllText(persistentPath);
        }

        TextAsset resourceChart = Resources.Load<TextAsset>($"Charts/{chartFileName}");
        if (resourceChart != null)
        {
            Debug.Log($"Loaded chart JSON from Resources/Charts/{chartFileName}");
            return resourceChart.text;
        }

        return null;
    }

    private void ApplyPerformanceSettings()
    {
        if (disableVSync)
        {
            QualitySettings.vSyncCount = 0;
        }

        Application.targetFrameRate = Mathf.Max(30, targetFrameRate);
    }

    private void ClearActiveNotes()
    {
        if (activeNotesByLane == null)
        {
            return;
        }

        for (int i = 0; i < activeNotesByLane.Length; i++)
        {
            List<RhythmNote> laneNotes = activeNotesByLane[i];
            for (int j = 0; j < laneNotes.Count; j++)
            {
                if (laneNotes[j] != null)
                {
                    Destroy(laneNotes[j].gameObject);
                }
            }

            laneNotes.Clear();
        }
    }
}
