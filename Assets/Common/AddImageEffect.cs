using UnityEngine;


public class AddImageEffect : MonoBehaviour
{
    private Material _shaderMaterial;
    private uint _sample;
    private RenderTexture _screenTexture;

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Initialize();
        _shaderMaterial.SetFloat("_Sample", _sample);
        _sample++;
        Graphics.Blit(src, dest, _shaderMaterial);
    }

    private void Update()
    {
        if (transform.hasChanged)
        {
            _sample = 0;
            transform.hasChanged = false;
        }
    }

    private void Initialize()
    {
        if (_shaderMaterial == null) _shaderMaterial = new Material(Shader.Find("Hidden/AddShader"));
    }
}
