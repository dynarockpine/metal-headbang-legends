using UnityEngine;
using UnityEngine.UI;

public class RhythmNote : MonoBehaviour
{
    [SerializeField] private Color tapColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private Color swipeColor = new Color(0.3f, 0.7f, 1f, 1f);
    [SerializeField] private Color rotateColor = new Color(0.3f, 1f, 0.3f, 1f);
    [SerializeField] private Color holdColor = new Color(1f, 0.7f, 0.2f, 1f);
    [SerializeField] private Vector2 defaultSize = new Vector2(100f, 100f);
    [SerializeField] private Vector2 swipeDownSize = new Vector2(120f, 150f);
    [SerializeField] private Vector2 holdSize = new Vector2(100f, 180f);

    private RectTransform rectTransform;
    private Image noteImage;
    private Text symbolText;
    private RhythmGameManager gameManager;
    private AudioSource musicSource;
    private float hitTime;
    private float spawnLeadTime;
    private Vector2 spawnPosition;
    private Vector2 targetPosition;
    private RhythmGameManager.NoteType noteType;
    private int lane;
    private bool initialized;
    private bool resolved;

    public float HitTime => hitTime;
    public RhythmGameManager.NoteType NoteType => noteType;
    public int Lane => lane;

    public void Initialize(RhythmGameManager manager, AudioSource source, float noteTime, float leadTime, Vector2 startPosition, Vector2 endPosition, RhythmGameManager.NoteType assignedNoteType, int assignedLane)
    {
        rectTransform = GetComponent<RectTransform>();
        noteImage = GetComponent<Image>();
        gameManager = manager;
        musicSource = source;
        hitTime = noteTime;
        spawnLeadTime = Mathf.Max(0.01f, leadTime);
        spawnPosition = startPosition;
        targetPosition = endPosition;
        noteType = assignedNoteType;
        lane = assignedLane;
        initialized = true;
        resolved = false;

        if (rectTransform != null)
        {
            rectTransform.anchoredPosition = spawnPosition;
        }

        if (noteImage != null)
        {
            noteImage.color = GetColor(assignedNoteType);
        }

        ApplyVisualByNoteType(assignedNoteType);
    }

    private void Update()
    {
        if (!initialized || resolved || musicSource == null || rectTransform == null)
        {
            return;
        }

        float progress = 1f - ((hitTime - musicSource.time) / spawnLeadTime);
        progress = Mathf.Clamp01(progress);
        rectTransform.anchoredPosition = Vector2.Lerp(spawnPosition, targetPosition, progress);

        if (gameManager != null && musicSource.time > hitTime + gameManager.GoodWindow)
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
