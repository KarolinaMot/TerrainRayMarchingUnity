using UnityEngine;
using static UnityEngine.Rendering.HableCurve;

public class ShaderInputs : MonoBehaviour
{
    public Texture2DArray blueNoise;

    private void Awake()
    {
        blueNoise = Resources.Load<Texture2DArray>("blueNoise64x64x64");
    }

    // Update is called once per frame
    private void Update()
    {
        Shader.SetGlobalInt("_FrameIndex", Time.frameCount);
        Shader.SetGlobalInt("_Fps", (int)(1.0f / Time.deltaTime));
        Shader.SetGlobalFloat("_DeltaTime", Time.deltaTime);
        Shader.SetGlobalFloat("_DSimTime", Time.time);

    }
}
