using UnityEngine;
using LibVLCSharp;

public class VLCUIControls : MonoBehaviour
{
    private const long MicrosecondsPerMillisecond = 1000;

    [SerializeField] private VLCMediaPlayer mediaPlayer;
    [SerializeField] private long seekTimeDeltaMs = 5000;

    public void SeekForward()
    {
        if (mediaPlayer != null)
            mediaPlayer.Seek(seekTimeDeltaMs * MicrosecondsPerMillisecond);
    }

    public void SeekBackward()
    {
        if (mediaPlayer != null)
            mediaPlayer.Seek(-seekTimeDeltaMs * MicrosecondsPerMillisecond);
    }
}
