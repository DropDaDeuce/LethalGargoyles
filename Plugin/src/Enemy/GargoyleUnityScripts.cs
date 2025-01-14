using UnityEngine;

namespace LethalGargoyles.src.Enemy
{
    public class LethalGargoylesSFX : MonoBehaviour
    {
        public AudioSource? audioSource;
        public AudioClip? audioClip;
        public void PlayStep()
        {
            if (audioSource == null || audioClip == null)
            {
                return;
            }
            audioSource.PlayOneShot(audioClip);
        }
    }
}
