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

    private readonly List<ChartNoteData> recordedNotes = new List<ChartNoteData>();
    private bool isRecording;

    private void Awake()
    {
        ResolveReferences();
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

        if (!isRecording)
        {
            return;
        }

        if (keyboard.aKey.wasPressedThisFrame)
        {
            RecordNote(RhythmGameManager.NoteType.SwipeLeftRight, 0);
        }

        if (keyboard.sKey.wasPressedThisFrame)
        {
            RecordNote(RhythmGameManager.NoteType.SwipeDown, 1);
        }

        if (keyboard.dKey.wasPressedThisFrame)
        {
            RecordNote(RhythmGameManager.NoteType.Tap, 2);
        }

        if (keyboard.fKey.wasPressedThisFrame)
        {
            RecordNote(RhythmGameManager.NoteType.Windmill, 3);
        }

        if (keyboard.gKey.wasPressedThisFrame)
        {
            RecordNote(RhythmGameManager.NoteType.Hold, 4);
        }

        if (keyboard.backspaceKey.wasPressedThisFrame)
        {
            RemoveLastNote();
        }

        if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            SaveChart();
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

        recordedNotes.Clear();
        isRecording = true;
        rhythmGameManager.RestartChartPlayback(true);
        Debug.Log("Chart recording started. A/S/D/F/G to record, Backspace to undo, Enter to save.");
    }

    public void SaveChart()
    {
        if (recordedNotes.Count == 0)
        {
            Debug.LogWarning("No notes recorded yet, so nothing was saved.");
            return;
        }

        recordedNotes.Sort((a, b) => a.time.CompareTo(b.time));

        ChartFileData chartFile = new ChartFileData
        {
            songName = songName,
            notes = recordedNotes.ToArray()
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

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        if (saveToAssetsChartsFolderInEditor)
        {
            AssetDatabase.Refresh();
        }
#endif

        Debug.Log($"Chart saved: {savePath}");
    }

    private void RecordNote(RhythmGameManager.NoteType noteType, int lane)
    {
        if (rhythmGameManager == null)
        {
            return;
        }

        float noteTime = rhythmGameManager.GetSongTime() + recordTimeOffset;
        noteTime = Mathf.Max(0f, noteTime);

        ChartNoteData note = new ChartNoteData
        {
            time = noteTime,
            type = noteType,
            lane = lane
        };

        recordedNotes.Add(note);
        Debug.Log($"Recorded {note.type} lane {note.lane} at {note.time:F3}s");
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
