using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text;

/// <summary>
/// AudioClip 의 길이를 수정(자르기/늘리기)하여 새로운 .wav 파일로 저장하는 에디터 윈도우.
///
/// 사용법: 상단 메뉴 Tools > Audio > AudioClip Length Editor 로 윈도우를 엽니다.
///
/// 주의: AudioClip 은 런타임에 길이를 직접 변경할 수 없습니다.
/// 이 도구는 원본의 샘플 데이터를 가공해 "새 클립"을 만든 뒤 프로젝트에 .wav 로 저장합니다.
/// 원본은 변경되지 않습니다.
/// </summary>
public class AudioClipLengthEditor : EditorWindow
{
    // 리사이즈 처리 방식
    private enum TrimAnchor { KeepStart, KeepEnd, KeepCenter }  // 길이를 줄일 때
    private enum PadMode { SilenceAtEnd, SilenceAtStart, Loop } // 길이를 늘릴 때

    private AudioClip sourceClip;
    private float targetLength = 1f;       // 목표 길이 (초)
    private TrimAnchor trimAnchor = TrimAnchor.KeepStart;
    private PadMode padMode = PadMode.SilenceAtEnd;
    private string outputFolder = "Assets";
    private string outputName = "";

    private Vector2 scroll;

    [MenuItem("Tools/Audio/AudioClip Length Editor")]
    private static void Open()
    {
        var window = GetWindow<AudioClipLengthEditor>("AudioClip Length");
        window.minSize = new Vector2(360, 320);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("원본 클립", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        sourceClip = (AudioClip)EditorGUILayout.ObjectField(
            "Source AudioClip", sourceClip, typeof(AudioClip), false);

        if (EditorGUI.EndChangeCheck() && sourceClip != null)
        {
            // 클립이 바뀌면 목표 길이와 출력 이름을 기본값으로 채움
            targetLength = sourceClip.length;
            outputName = sourceClip.name + "_resized";
        }

        if (sourceClip == null)
        {
            EditorGUILayout.HelpBox("길이를 수정할 AudioClip 을 지정하세요.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        DrawClipInfo();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("목표 길이", EditorStyles.boldLabel);

        targetLength = EditorGUILayout.FloatField("Target Length (초)", targetLength);
        targetLength = Mathf.Max(0.001f, targetLength);

        int targetSamples = Mathf.RoundToInt(targetLength * sourceClip.frequency);
        EditorGUILayout.LabelField("  → 목표 샘플 수", targetSamples.ToString("N0"));

        EditorGUILayout.Space();

        // 줄일지 늘릴지에 따라 다른 옵션 노출
        if (targetSamples < sourceClip.samples)
        {
            EditorGUILayout.LabelField("자르기 옵션 (길이 단축)", EditorStyles.boldLabel);
            trimAnchor = (TrimAnchor)EditorGUILayout.EnumPopup("Trim Anchor", trimAnchor);
            EditorGUILayout.HelpBox(TrimAnchorHelp(trimAnchor), MessageType.None);
        }
        else if (targetSamples > sourceClip.samples)
        {
            EditorGUILayout.LabelField("채우기 옵션 (길이 연장)", EditorStyles.boldLabel);
            padMode = (PadMode)EditorGUILayout.EnumPopup("Pad Mode", padMode);
            EditorGUILayout.HelpBox(PadModeHelp(padMode), MessageType.None);
        }
        else
        {
            EditorGUILayout.HelpBox("목표 길이가 원본과 동일합니다.", MessageType.None);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("저장 위치", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        outputFolder = EditorGUILayout.TextField("Folder", outputFolder);
        if (GUILayout.Button("...", GUILayout.Width(30)))
        {
            string picked = EditorUtility.OpenFolderPanel("저장 폴더 선택", Application.dataPath, "");
            if (!string.IsNullOrEmpty(picked))
                outputFolder = ToProjectRelativePath(picked);
        }
        EditorGUILayout.EndHorizontal();

        outputName = EditorGUILayout.TextField("File Name", outputName);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(outputName)))
        {
            if (GUILayout.Button("새 클립으로 저장 (.wav)", GUILayout.Height(32)))
            {
                ProcessAndSave(targetSamples);
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawClipInfo()
    {
        EditorGUILayout.HelpBox(
            $"길이: {sourceClip.length:F3} 초\n" +
            $"샘플 수: {sourceClip.samples:N0} (채널당)\n" +
            $"채널: {sourceClip.channels}\n" +
            $"주파수: {sourceClip.frequency:N0} Hz",
            MessageType.None);
    }

    private static string TrimAnchorHelp(TrimAnchor a)
    {
        switch (a)
        {
            case TrimAnchor.KeepStart:  return "앞부분을 유지하고 뒤를 잘라냅니다.";
            case TrimAnchor.KeepEnd:    return "뒷부분을 유지하고 앞을 잘라냅니다.";
            case TrimAnchor.KeepCenter: return "가운데를 기준으로 양쪽을 잘라냅니다.";
            default: return "";
        }
    }

    private static string PadModeHelp(PadMode m)
    {
        switch (m)
        {
            case PadMode.SilenceAtEnd:   return "끝에 무음을 추가해 길이를 늘립니다.";
            case PadMode.SilenceAtStart: return "앞에 무음을 추가해 길이를 늘립니다.";
            case PadMode.Loop:           return "원본을 반복 재생해 목표 길이를 채웁니다.";
            default: return "";
        }
    }

    private void ProcessAndSave(int targetSamples)
    {
        int channels = sourceClip.channels;
        int frequency = sourceClip.frequency;
        int srcSamples = sourceClip.samples;

        // 원본 데이터 읽기 (인터리브된 float 배열)
        float[] srcData = new float[srcSamples * channels];
        if (!sourceClip.GetData(srcData, 0))
        {
            EditorUtility.DisplayDialog("실패",
                "원본 클립의 샘플 데이터를 읽을 수 없습니다.\n" +
                "Import Settings 에서 'Load Type' 을 Decompress On Load 로,\n" +
                "'Preload Audio Data' 를 켜고 다시 시도하세요.",
                "확인");
            return;
        }

        float[] dstData = new float[targetSamples * channels];

        if (targetSamples <= srcSamples)
        {
            // 자르기
            int startSample = 0;
            switch (trimAnchor)
            {
                case TrimAnchor.KeepStart:  startSample = 0; break;
                case TrimAnchor.KeepEnd:    startSample = srcSamples - targetSamples; break;
                case TrimAnchor.KeepCenter: startSample = (srcSamples - targetSamples) / 2; break;
            }
            Array.Copy(srcData, startSample * channels, dstData, 0, targetSamples * channels);
        }
        else
        {
            // 채우기
            switch (padMode)
            {
                case PadMode.SilenceAtEnd:
                    // 앞에 원본 복사, 나머지는 0(무음)으로 자동 초기화됨
                    Array.Copy(srcData, 0, dstData, 0, srcData.Length);
                    break;

                case PadMode.SilenceAtStart:
                    int offset = (targetSamples - srcSamples) * channels;
                    Array.Copy(srcData, 0, dstData, offset, srcData.Length);
                    break;

                case PadMode.Loop:
                    for (int i = 0; i < dstData.Length; i++)
                        dstData[i] = srcData[i % srcData.Length];
                    break;
            }
        }

        // 새 클립 생성 (미리듣기/검증용)
        AudioClip newClip = AudioClip.Create(outputName, targetSamples, channels, frequency, false);
        newClip.SetData(dstData, 0);

        // .wav 로 저장
        string folder = string.IsNullOrEmpty(outputFolder) ? "Assets" : outputFolder;
        string fileName = outputName.EndsWith(".wav") ? outputName : outputName + ".wav";
        string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            Path.Combine(folder, fileName).Replace("\\", "/"));

        string absolutePath = Path.Combine(
            Application.dataPath, assetPath.Substring("Assets".Length).TrimStart('/', '\\'));

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
        WavUtility.Save(absolutePath, dstData, channels, frequency);

        AssetDatabase.Refresh();

        var saved = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        EditorGUIUtility.PingObject(saved);
        Selection.activeObject = saved;

        Debug.Log($"[AudioClipLengthEditor] 저장 완료: {assetPath} " +
                  $"({targetLength:F3}초, {targetSamples:N0} 샘플)");
    }

    private static string ToProjectRelativePath(string absolute)
    {
        absolute = absolute.Replace("\\", "/");
        string dataPath = Application.dataPath.Replace("\\", "/");
        if (absolute.StartsWith(dataPath))
            return "Assets" + absolute.Substring(dataPath.Length);
        return "Assets";
    }
}

/// <summary>
/// float 샘플 배열을 16-bit PCM WAV 파일로 저장하는 간단한 유틸리티.
/// </summary>
public static class WavUtility
{
    public static void Save(string path, float[] interleavedSamples, int channels, int frequency)
    {
        using (var fs = new FileStream(path, FileMode.Create))
        using (var writer = new BinaryWriter(fs))
        {
            int sampleCount = interleavedSamples.Length;
            int byteRate = frequency * channels * 2; // 16-bit = 2 bytes
            int dataSize = sampleCount * 2;

            // RIFF 헤더
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt 청크
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                       // 청크 크기
            writer.Write((short)1);                 // PCM
            writer.Write((short)channels);
            writer.Write(frequency);
            writer.Write(byteRate);
            writer.Write((short)(channels * 2));    // block align
            writer.Write((short)16);                // bits per sample

            // data 청크
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataSize);

            // float(-1~1) → 16-bit PCM 변환
            for (int i = 0; i < sampleCount; i++)
            {
                float clamped = Mathf.Clamp(interleavedSamples[i], -1f, 1f);
                short value = (short)(clamped * short.MaxValue);
                writer.Write(value);
            }
        }
    }
}
