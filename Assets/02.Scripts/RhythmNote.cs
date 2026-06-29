using UnityEngine;
using UnityEngine.UI;

public class RhythmNote : MonoBehaviour
{
    private const string Lane1SpritePath = "Notes/note-purple";
    private const string Lane2SpritePath = "Notes/note-blue";
    private const string Lane3SpritePath = "Notes/note-red";
    private const string Lane4SpritePath = "Notes/note-green";
    private const string Lane5SpritePath = "Notes/note-yellow";

    [SerializeField] private Color tapColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color swipeColor = new Color(0.3f, 0.7f, 1f, 1f);
    [SerializeField] private Color rotateColor = new Color(0.3f, 1f, 0.3f, 1f);
    [SerializeField] private Color holdColor = new Color(1f, 0.7f, 0.2f, 1f);
    [SerializeField] private Vector2 noteSize = new Vector2(120f, 120f);
    [Header("Perspective")]
    [SerializeField, Range(0.4f, 1.4f)] private float farWidthScaleMultiplier = 0.88f;
    [SerializeField, Range(0.4f, 1.4f)] private float farHeightScaleMultiplier = 0.68f;
    [SerializeField, Range(0.4f, 1.4f)] private float hitWidthScaleMultiplier = 1f;
    [SerializeField, Range(0.4f, 1.4f)] private float hitHeightScaleMultiplier = 1f;
    [SerializeField, Range(-80f, 80f)] private float noteTiltXDegrees = 55f;
    [SerializeField, Range(0.05f, 1f)] private float missDespawnDelay = 0.3f;
    [SerializeField] private Sprite lane1Sprite;
    [SerializeField] private Sprite lane2Sprite;
    [SerializeField] private Sprite lane3Sprite;
    [SerializeField] private Sprite lane4Sprite;
    [SerializeField] private Sprite lane5Sprite;

    private RectTransform rectTransform;
    private Image noteImage;
    private Text symbolText;
    private RhythmGameManager gameManager;
    private AudioSource musicSource;
    private float hitTime;
    private float spawnLeadTime;
    private float spawnScale;
    private float hitScale;
    private Vector2 spawnPosition;
    private Vector2 targetPosition;
    private Vector2 travelDirection;
    private float travelDistance;
    private RhythmGameManager.NoteType noteType;
    private int lane;
    private bool initialized;
    private bool resolved;
    private bool missRegistered;

    public float HitTime => hitTime;
    public RhythmGameManager.NoteType NoteType => noteType;
    public int Lane => lane;

    public void Initialize(RhythmGameManager manager, AudioSource source, float noteTime, float leadTime, Vector2 startPosition, Vector2 endPosition, float startScale, float endScale, RhythmGameManager.NoteType assignedNoteType, int assignedLane)
    {
        rectTransform = GetComponent<RectTransform>();
        noteImage = GetComponent<Image>();
        gameManager = manager;
        musicSource = source;
        hitTime = noteTime;
        spawnLeadTime = Mathf.Max(0.01f, leadTime);
        spawnScale = Mathf.Max(0.01f, startScale);
        hitScale = Mathf.Max(spawnScale, endScale);
        spawnPosition = startPosition;
        targetPosition = endPosition;
        Vector2 travelVector = targetPosition - spawnPosition;
        travelDistance = travelVector.magnitude;
        travelDirection = travelDistance > 0.001f ? travelVector / travelDistance : Vector2.down;
        noteType = assignedNoteType;
        lane = assignedLane;
        initialized = true;
        resolved = false;
        missRegistered = false;

        if (rectTransform != null)
        {
            rectTransform.pivot = new Vector2(0.5f, 0f);
            rectTransform.anchoredPosition = spawnPosition;
            ApplyPerspectiveTransform(0f);
        }

        if (noteImage != null)
        {
            noteImage.preserveAspect = false;
        }

        ApplyVisual();
    }

    private void Update()
    {
        if (!initialized || resolved || musicSource == null || rectTransform == null)
        {
            return;
        }

        float songTime = gameManager != null ? gameManager.GetSongTime() : musicSource.time;
        float progress = GetVisualProgress(songTime);
        ApplyPerspectiveTransform(progress);

        float missTime = hitTime + (gameManager != null ? gameManager.GoodWindow : 0f);
        if (!missRegistered && songTime > missTime)
        {
            RegisterMiss();
        }

        if (missRegistered && songTime > missTime + missDespawnDelay)
        {
            Destroy(gameObject);
        }
    }

    public void Hit()
    {
        Resolve(true);
    }

    private void Resolve(bool wasHit)
    {
        if (resolved)
        {
            return;
        }

        resolved = true;
        if (gameManager != null)
        {
            gameManager.NotifyNoteResolved(this, wasHit);
        }

        Destroy(gameObject);
    }

    private void RegisterMiss()
    {
        if (missRegistered)
        {
            return;
        }

        missRegistered = true;
        if (gameManager != null)
        {
            gameManager.NotifyNoteResolved(this, false);
        }
    }

    private Color GetLaneColor(int laneIndex)
    {
        switch (laneIndex)
        {
            case 0:
                return new Color(0.69f, 0.27f, 1f, 1f);
            case 1:
                return swipeColor;
            case 2:
                return tapColor;
            case 3:
                return rotateColor;
            case 4:
                return holdColor;
            default:
                return Color.white;
        }
    }

    private void ApplyVisual()
    {
        if (rectTransform != null)
        {
            rectTransform.sizeDelta = noteSize;
        }

        if (noteImage != null)
        {
            EnsureNoteSpritesLoaded();
            Sprite noteSprite = GetSpriteForLane(lane);
            noteImage.sprite = noteSprite;
            noteImage.color = noteSprite != null ? Color.white : GetLaneColor(lane);
        }

        RemoveSymbolText();
    }

    private void EnsureNoteSpritesLoaded()
    {
        if (lane1Sprite == null)
        {
            lane1Sprite = Resources.Load<Sprite>(Lane1SpritePath);
        }

        if (lane2Sprite == null)
        {
            lane2Sprite = Resources.Load<Sprite>(Lane2SpritePath);
        }

        if (lane3Sprite == null)
        {
            lane3Sprite = Resources.Load<Sprite>(Lane3SpritePath);
        }

        if (lane4Sprite == null)
        {
            lane4Sprite = Resources.Load<Sprite>(Lane4SpritePath);
        }

        if (lane5Sprite == null)
        {
            lane5Sprite = Resources.Load<Sprite>(Lane5SpritePath);
        }
    }

    private Sprite GetSpriteForLane(int laneIndex)
    {
        switch (laneIndex)
        {
            case 0:
                return lane1Sprite;
            case 1:
                return lane2Sprite;
            case 2:
                return lane3Sprite;
            case 3:
                return lane4Sprite;
            case 4:
                return lane5Sprite;
            default:
                return null;
        }
    }

    private void ApplyPerspectiveTransform(float progress)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchoredPosition = GetPositionAtProgress(progress);

        float scaleProgress = Mathf.Clamp01(progress);
        float uniformScale = Mathf.Lerp(spawnScale, hitScale, scaleProgress);
        float widthScale = Mathf.Lerp(farWidthScaleMultiplier, hitWidthScaleMultiplier, scaleProgress);
        float heightScale = Mathf.Lerp(farHeightScaleMultiplier, hitHeightScaleMultiplier, scaleProgress);

        rectTransform.localScale = new Vector3(
            uniformScale * widthScale,
            uniformScale * heightScale,
            1f);

        rectTransform.localRotation = Quaternion.Euler(noteTiltXDegrees, 0f, 0f);
    }

    private float GetVisualProgress(float songTime)
    {
        float elapsedSinceSpawn = songTime - (hitTime - spawnLeadTime);
        return elapsedSinceSpawn / spawnLeadTime;
    }

    private Vector2 GetPositionAtProgress(float progress)
    {
        if (travelDistance <= 0.001f)
        {
            return targetPosition;
        }

        float traveledDistance = Mathf.Max(0f, progress) * travelDistance;
        return spawnPosition + (travelDirection * traveledDistance);
    }

    private void EnsureSymbolText(string symbol, int fontSize, Vector2 anchoredPosition)
    {
        if (symbolText == null)
        {
            GameObject textObject = new GameObject("NoteSymbol", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(transform, false);
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.sizeDelta = new Vector2(120f, 120f);

            symbolText = textObject.GetComponent<Text>();
            symbolText.font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Verdana", "Segoe UI" }, fontSize);
            if (symbolText.font == null)
            {
                symbolText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }

            symbolText.alignment = TextAnchor.MiddleCenter;
            symbolText.raycastTarget = false;
        }

        RectTransform symbolRect = symbolText.rectTransform;
        symbolRect.anchoredPosition = anchoredPosition;
        symbolText.text = symbol;
        symbolText.fontSize = fontSize;
        symbolText.fontStyle = FontStyle.Bold;
        symbolText.color = new Color(1f, 1f, 1f, 0.95f);
    }

    private void RemoveSymbolText()
    {
        if (symbolText != null)
        {
            Destroy(symbolText.gameObject);
            symbolText = null;
        }
    }
}
