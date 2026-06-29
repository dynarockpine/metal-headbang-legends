using UnityEngine;
using UnityEngine.UI;

public class RhythmNote : MonoBehaviour
{
    private const string TapSpritePath = "Notes/note-tap";
    private const string SwipeLeftRightSpritePath = "Notes/note-swipeleftright";
    private const string SwipeDownSpritePath = "Notes/note-swipedown";
    private const string WindmillSpritePath = "Notes/note-windmill";
    private const string HoldSpritePath = "Notes/note-hold";

    [SerializeField] private Color tapColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color swipeColor = new Color(0.3f, 0.7f, 1f, 1f);
    [SerializeField] private Color rotateColor = new Color(0.3f, 1f, 0.3f, 1f);
    [SerializeField] private Color holdColor = new Color(1f, 0.7f, 0.2f, 1f);
    [SerializeField] private Vector2 defaultSize = new Vector2(100f, 100f);
    [SerializeField] private Vector2 swipeDownSize = new Vector2(120f, 150f);
    [SerializeField] private Vector2 holdSize = new Vector2(100f, 180f);
    [Header("Perspective")]
    [SerializeField, Range(0.4f, 1.4f)] private float farWidthScaleMultiplier = 0.88f;
    [SerializeField, Range(0.4f, 1.4f)] private float farHeightScaleMultiplier = 0.68f;
    [SerializeField, Range(0.4f, 1.4f)] private float hitWidthScaleMultiplier = 1f;
    [SerializeField, Range(0.4f, 1.4f)] private float hitHeightScaleMultiplier = 1f;
    [SerializeField, Range(-80f, 80f)] private float noteTiltXDegrees = 55f;
    [SerializeField] private Sprite tapSprite;
    [SerializeField] private Sprite swipeLeftRightSprite;
    [SerializeField] private Sprite swipeDownSprite;
    [SerializeField] private Sprite windmillSprite;
    [SerializeField] private Sprite holdSprite;

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
    private RhythmGameManager.NoteType noteType;
    private int lane;
    private bool initialized;
    private bool resolved;

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
        noteType = assignedNoteType;
        lane = assignedLane;
        initialized = true;
        resolved = false;

        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = spawnPosition;
            ApplyPerspectiveTransform(0f);
        }

        if (noteImage != null)
        {
            noteImage.preserveAspect = true;
        }

        ApplyVisualByNoteType(assignedNoteType);
    }

    private void Update()
    {
        if (!initialized || resolved || musicSource == null || rectTransform == null)
        {
            return;
        }

        float songTime = gameManager != null ? gameManager.GetSongTime() : musicSource.time;
        float progress = 1f - ((hitTime - songTime) / spawnLeadTime);
        progress = Mathf.Clamp01(progress);
        ApplyPerspectiveTransform(progress);

        if (gameManager != null && songTime > hitTime + gameManager.GoodWindow)
        {
            Resolve(false);
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

    private Color GetColor(RhythmGameManager.NoteType noteType)
    {
        switch (noteType)
        {
            case RhythmGameManager.NoteType.SwipeLeftRight:
            case RhythmGameManager.NoteType.SwipeDown:
                return swipeColor;
            case RhythmGameManager.NoteType.Windmill:
                return rotateColor;
            case RhythmGameManager.NoteType.Hold:
                return holdColor;
            default:
                return tapColor;
        }
    }

    private void ApplyVisualByNoteType(RhythmGameManager.NoteType assignedNoteType)
    {
        if (rectTransform != null)
        {
            rectTransform.sizeDelta = GetSizeForNoteType(assignedNoteType);
        }

        EnsureNoteSpritesLoaded();

        if (noteImage != null)
        {
            Sprite noteSprite = GetSpriteForNoteType(assignedNoteType);
            if (noteSprite != null)
            {
                noteImage.sprite = noteSprite;
                noteImage.color = Color.white;
                RemoveSymbolText();
                return;
            }

            noteImage.color = GetColor(assignedNoteType);
        }

        switch (assignedNoteType)
        {
            case RhythmGameManager.NoteType.SwipeDown:
                EnsureSymbolText("↓", 88, new Vector2(0f, -2f));
                break;
            case RhythmGameManager.NoteType.Windmill:
                EnsureSymbolText("↻", 84, new Vector2(0f, 0f));
                break;
            case RhythmGameManager.NoteType.SwipeLeftRight:
                EnsureSymbolText("↔", 78, new Vector2(0f, 0f));
                break;
            case RhythmGameManager.NoteType.Hold:
                EnsureSymbolText("!", 90, new Vector2(0f, 0f));
                break;
            default:
                RemoveSymbolText();
                break;
        }
    }

    private void EnsureNoteSpritesLoaded()
    {
        if (tapSprite == null)
        {
            tapSprite = Resources.Load<Sprite>(TapSpritePath);
        }

        if (swipeLeftRightSprite == null)
        {
            swipeLeftRightSprite = Resources.Load<Sprite>(SwipeLeftRightSpritePath);
        }

        if (swipeDownSprite == null)
        {
            swipeDownSprite = Resources.Load<Sprite>(SwipeDownSpritePath);
        }

        if (windmillSprite == null)
        {
            windmillSprite = Resources.Load<Sprite>(WindmillSpritePath);
        }

        if (holdSprite == null)
        {
            holdSprite = Resources.Load<Sprite>(HoldSpritePath);
        }
    }

    private Sprite GetSpriteForNoteType(RhythmGameManager.NoteType assignedNoteType)
    {
        switch (assignedNoteType)
        {
            case RhythmGameManager.NoteType.SwipeLeftRight:
                return swipeLeftRightSprite;
            case RhythmGameManager.NoteType.SwipeDown:
                return swipeDownSprite;
            case RhythmGameManager.NoteType.Windmill:
                return windmillSprite;
            case RhythmGameManager.NoteType.Hold:
                return holdSprite;
            default:
                return tapSprite;
        }
    }

    private void ApplyPerspectiveTransform(float progress)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchoredPosition = Vector2.Lerp(spawnPosition, targetPosition, progress);

        float uniformScale = Mathf.Lerp(spawnScale, hitScale, progress);
        float widthScale = Mathf.Lerp(farWidthScaleMultiplier, hitWidthScaleMultiplier, progress);
        float heightScale = Mathf.Lerp(farHeightScaleMultiplier, hitHeightScaleMultiplier, progress);

        rectTransform.localScale = new Vector3(
            uniformScale * widthScale,
            uniformScale * heightScale,
            1f);

        rectTransform.localRotation = Quaternion.Euler(noteTiltXDegrees, 0f, 0f);
    }

    private Vector2 GetSizeForNoteType(RhythmGameManager.NoteType assignedNoteType)
    {
        switch (assignedNoteType)
        {
            case RhythmGameManager.NoteType.SwipeDown:
                return swipeDownSize;
            case RhythmGameManager.NoteType.Hold:
                return holdSize;
            default:
                return defaultSize;
        }
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
