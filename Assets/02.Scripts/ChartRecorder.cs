using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif
#endif

public class ChartRecorder : MonoBehaviour
{
    [SerializeField] private RhythmGameManager rhythmGameManager;
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private string songName = "Racing";
    [SerializeField] private bool saveToAssetsChartsFolderInEditor = true;
    [SerializeField] private float recordTimeOffset = 0f;
    [SerializeField] private Key punchInKey = Key.P;
    [SerializeField] private Key punchOutKey = Key.O;

    private readonly List<ChartNoteData> workingNotes = new List<ChartNoteData>();
    private readonly List<ChartNoteData> recordedNotes = new List<ChartNoteData>();
    private bool isRecording;
    private bool isFullRewriteMode;
    private bool isPunchInActive;
    private float punchStartTime = -1f;
    private float punchEndTime = -1f;

    private void Awake()
    {
        ResolveReferences();
        LoadWorkingNotesFromChart();
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            StartRecording();
        }

        if (keyboard[punchInKey].wasPressedThisFrame)
        {
            StartPunchInRecording();
        }

        if (keyboard[punchOutKey].wasPressedThisFrame)
        {
            EndPunchInRecording();
        }

        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            SaveChart();
        }

        if (!isRecording)
        {
            return;
        }

        if (keyboard.aKey.wasPressedThisFrame)
        {
            RecordLaneNote(0);
        }

        if (keyboard.sKey.wasPressedThisFrame)
        {
            RecordLaneNote(1);
        }

        if (keyboard.dKey.wasPressedThisFrame)
        {
            RecordLaneNote(2);
        }

        if (keyboard.fKey.wasPressedThisFrame)
        {
            RecordLaneNote(3);
        }

        if (keyboard.gKey.wasPressedThisFrame)
        {
            RecordLaneNote(4);
        }

        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            RemoveLastNote();
        }
#endif
    }

    private void ResolveReferences()
    {
        if (rhythmGameManager == null)
        {
            rhythmGameManager = FindFirstObjectByType<RhythmGameManager>();
        }

        if (musicSource == null && rhythmGameManager != null)
        {
            musicSource = rhythmGameManager.GetComponent<AudioSource>();
        }

        if (musicSource == null)
        {
            musicSource = FindFirstObjectByType<AudioSource>();
        }
    }

    public void StartRecording()
    {
        ResolveReferences();

        if (rhythmGameManager == null || musicSource == null || musicSource.clip == null)
        {
            Debug.LogWarning("ChartRecorder could not start because RhythmGameManager or AudioSource is missing.");
            return;
        }

        workingNotes.Clear();
        recordedNotes.Clear();
        isRecording = true;
        isFullRewriteMode = true;
        isPunchInActive = false;
        punchStartTime = -1f;
        punchEndTime = -1f;
        rhythmGameManager.RestartChartPlayback(true);
        Debug.Log("Full chart recording started. A/S/D/F = tap lanes 1-4, G = hold lane 5, Backspace = undo, P = punch in, O = punch out, Enter = save.");
    }

    public void StartPunchInRecording()
    {
        ResolveReferences();

        if (rhythmGameManager == null || musicSource == null || musicSource.clip == null)
        {
            Debug.LogWarning("ChartRecorder could not start punch-in because RhythmGameManager or AudioSource is missing.");
            return;
        }

        if (isPunchInActive)
        {
            Debug.LogWarning("Punch-in recording is already active.");
            return;
        }

        if (workingNotes.Count == 0)
        {
            LoadWorkingNotesFromChart();
        }

        recordedNotes.Clear();
        isRecording = true;
        isFullRewriteMode = false;
        isPunchInActive = true;
        punchStartTime = GetCurrentRecordTime();
        punchEndTime = -1f;

        Debug.Log($"Punch-in started at {punchStartTime:F3}s. Record replacement notes now, then press {punchOutKey} to punch out.");
    }

    public void EndPunchInRecording()
    {
        if (!isPunchInActive)
        {
            return;
        }

        punchEndTime = Mathf.Max(GetCurrentRecordTime(), punchStartTime);
        CommitPunchRange();
        isRecording = false;
        isPunchInActive = false;
        recordedNotes.Clear();

        Debug.Log($"Punch-out finished at {punchEndTime:F3}s. Section replaced. Press Enter to save, or {punchInKey} to patch another section.");
    }

    public void SaveChart()
    {
        if (isPunchInActive)
        {
            EndPunchInRecording();
        }

        List<ChartNoteData> notesToSave = new List<ChartNoteData>();

        if (isFullRewriteMode)
        {
            notesToSave.AddRange(recordedNotes);
        }
        else
        {
            notesToSave.AddRange(workingNotes);
        }

        if (notesToSave.Count == 0)
        {
            Debug.LogWarning("No notes recorded yet, so nothing was saved.");
            return;
        }

        notesToSave.Sort((a, b) => a.time.CompareTo(b.time));

        ChartFileData chartFile = new ChartFileData
        {
            songName = songName,
            notes = notesToSave.ToArray()
        };

        string json = JsonUtility.ToJson(chartFile, true);
        string savePath = GetSavePath();
        string directoryPath = Path.GetDirectoryName(savePath);

        if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        File.WriteAllText(savePath, json);
        isRecording = false;
        isFullRewriteMode = false;
        isPunchInActive = false;
        workingNotes.Clear();
        workingNotes.AddRange(notesToSave);
        recordedNotes.Clear();

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        if (saveToAssetsChartsFolderInEditor)
        {
            AssetDatabase.Refresh();
        }
#endif

        Debug.Log($"Chart saved: {savePath} ({notesToSave.Count} notes)");
    }

    private void RecordNote(RhythmGameManager.NoteType noteType, int lane)
    {
        if (rhythmGameManager == null)
        {
            return;
        }

        float noteTime = GetCurrentRecordTime();

        ChartNoteData note = new ChartNoteData
        {
            time = noteTime,
            type = noteType,
            lane = lane
        };

        recordedNotes.Add(note);
        Debug.Log($"Recorded {note.type} lane {note.lane} at {note.time:F3}s");
    }

    private void RecordLaneNote(int lane)
    {
        RhythmGameManager.NoteType noteType = lane == 4
            ? RhythmGameManager.NoteType.Hold
            : RhythmGameManager.NoteType.Tap;

        RecordNote(noteType, lane);
    }

    private void RemoveLastNote()
    {
        if (recordedNotes.Count == 0)
        {
            return;
        }

        ChartNoteData removed = recordedNotes[recordedNotes.Count - 1];
        recordedNotes.RemoveAt(recordedNotes.Count - 1);
        Debug.Log($"Removed {removed.type} lane {removed.lane} at {removed.time:F3}s");
    }

    private float GetCurrentRecordTime()
    {
        if (rhythmGameManager == null)
        {
            return 0f;
        }

        float noteTime = rhythmGameManager.GetSongTime() + recordTimeOffset;
        return Mathf.Max(0f, noteTime);
    }

    private void CommitPunchRange()
    {
        if (punchStartTime < 0f)
        {
            return;
        }

        float rangeStart = Mathf.Min(punchStartTime, punchEndTime);
        float rangeEnd = Mathf.Max(punchStartTime, punchEndTime);

        workingNotes.RemoveAll(note => note.time >= rangeStart && note.time <= rangeEnd);
        workingNotes.AddRange(recordedNotes);
        workingNotes.Sort((a, b) => a.time.CompareTo(b.time));
    }

    private void LoadWorkingNotesFromChart()
    {
        workingNotes.Clear();

        string json = TryLoadChartJson();
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        ChartFileData chartFile = JsonUtility.FromJson<ChartFileData>(json);
        if (chartFile == null || chartFile.notes == null)
        {
            return;
        }

        workingNotes.AddRange(chartFile.notes);
        workingNotes.Sort((a, b) => a.time.CompareTo(b.time));
    }

    private string TryLoadChartJson()
    {
        string preferredSavePath = GetSavePath();
        if (File.Exists(preferredSavePath))
        {
            return File.ReadAllText(preferredSavePath);
        }

        string persistentPath = Path.Combine(Application.persistentDataPath, $"{songName}.json");
        if (File.Exists(persistentPath))
        {
            return File.ReadAllText(persistentPath);
        }

#if UNITY_EDITOR
        string assetChartPath = Path.Combine(Application.dataPath, "04.Charts", $"{songName}.json");
        if (File.Exists(assetChartPath))
        {
            return File.ReadAllText(assetChartPath);
        }
#endif

        return null;
    }

    private string GetSavePath()
    {
#if UNITY_EDITOR
        if (saveToAssetsChartsFolderInEditor)
        {
            return Path.Combine(Application.dataPath, "04.Charts", $"{songName}.json");
        }
#endif

        return Path.Combine(Application.persistentDataPath, $"{songName}.json");
    }
}

[Serializable]
public class ChartFileData
{
    public string songName;
    public ChartNoteData[] notes;
}

[Serializable]
public struct ChartNoteData
{
    public float time;
    public RhythmGameManager.NoteType type;
    public int lane;
}
