using UnityEngine;

internal sealed class LocalBirchBattleBackdrop : MonoBehaviour
{
    [SerializeField] private Transform clearing;
    [SerializeField] private Transform mist;
    [SerializeField] private Transform foreground;

    private Camera stageCamera;
    private float fittedAspect;
    private float clearingRatio = 941f / 1672f;

    internal void SetCamera(Camera camera, Texture2D storyPainting = null)
    {
        stageCamera = camera;
        if (storyPainting != null)
        {
            var renderer = clearing.GetComponent<Renderer>();
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetTexture("_MainTex", storyPainting);
            renderer.SetPropertyBlock(properties);
            clearingRatio = (float)storyPainting.height / storyPainting.width;
            foreground.gameObject.SetActive(false);
            var ambient = GetComponent<SfxSpawn>();
            if (ambient != null)
            {
                ambient.StopAllCoroutines();
                ambient.enabled = false;
            }
        }
        FitLayers();
    }

    private void LateUpdate()
    {
        if (stageCamera != null && !Mathf.Approximately(fittedAspect, stageCamera.aspect)) FitLayers();
    }

    private void FitLayers()
    {
        fittedAspect = stageCamera.aspect;
        // The original orthographic stage spans 720 authored units vertically.
        // Leave room for camera motion while keeping the cutouts near the viewport edge.
        float frameWidth = 720f * fittedAspect;
        float clearingWidth = Mathf.Max(1610f, frameWidth + 260f);
        clearingWidth = Mathf.Max(clearingWidth, 850f / clearingRatio);
        clearing.localScale = new Vector3(clearingWidth, clearingWidth * clearingRatio, 1f);
        mist.localScale = new Vector3(clearingWidth, 880f, 1f);
        foreground.localScale = new Vector3(frameWidth + 108f, 786f, 1f);
    }
}
