using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Random = UnityEngine.Random;

[ExecuteInEditMode, ImageEffectAllowedInSceneView]
public class RayTracingController : MonoBehaviour
{
    public string RandomSeed;
    public ComputeShader ScreenShader;
    public Texture SkyTexture;
    [Range(0, 1f)] public float SkyIntensity = 1f;
    
    [Range(1, 10)] public int MaxBounce = 8;

    public Light DirectionalLight;
    public Color GroundColor = Color.white;
    
    public int SphereAmount = 50;
    public int WorldSize = 10;
    
    public float MinRadius = 0.1f;
    public float MaxRadius = 1f;
    public float Spacing = 0.05f;
    
    private RenderTexture _screenTexture;
    private RenderTexture _convergedTexture;

    private Camera _camera;
    private Sphere[] _spheres;

    private readonly List<ComputeBuffer> _buffersToDispose = new List<ComputeBuffer>();
    private System.Random _random;

    private int _lastSphereAmount;
    private int _lastWorldSize;

    private Material _shaderMaterial;
    private uint _sample;
    
    struct Sphere
    {
        public Vector3 Position;
        public float Radius;
        public Vector3 Color;
        public Vector3 Specular;
        public float Smoothness;
        public Vector3 Emission;

        public static int GetSize()
        {
            return sizeof(float) * 14;
        }
    }

    private void OnEnable()
    {
        CreateScene();
    }

    private void Update()
    {
        if (SphereAmount != _lastSphereAmount || WorldSize != _lastWorldSize)
        {
            CreateScene();
        }
        
        if (transform.hasChanged)
        {
            _sample = 0;
            transform.hasChanged = false;
        }
    }
    private void CreateScene()
    {
        if (SphereAmount <= 0) return;
        _lastSphereAmount = SphereAmount;
        _lastWorldSize = WorldSize;
        _random = new System.Random(string.IsNullOrEmpty(RandomSeed) ? 0 : RandomSeed.GetHashCode());
        _spheres = GenerateRandomSpheres();
    }

    private Sphere[] GenerateRandomSpheres()
    {
        var spheres = new Sphere[SphereAmount];
        for (int i = 0; i < SphereAmount; i++)
        {
            var radius = 0f;
            var position = Vector3.zero;
            var isMetal = _random.NextDouble() < 0.5;
            var isEmitting = _random.NextDouble() < 0.2;
            var color = GenerateRandomColor();
            
            if (!GeneratePosition(spheres, out position, out radius)) break;
            
            spheres[i] = new Sphere()
            {
                Position = position,
                Radius = radius,
                Color = color,
                Specular = isMetal ? color : Vector3.one * 0.04f,
                Smoothness = NextFloat(),
                Emission = isEmitting ? color : Vector3.zero
            };
        }

        return spheres;
    }

    private bool GeneratePosition(Sphere[] spheres, out Vector3 position, out float radius)
    {
        var isValid = false;
        var maxTries = 30;
        var worldSpan = WorldSize / 2f;
        
        position = Vector3.zero;
        radius = 0;
        
        while (!isValid && maxTries-- > 0)
        {
            position = new Vector3(GetRandomRange(-worldSpan, worldSpan), 1, GetRandomRange(-worldSpan, worldSpan));

            // make sure not to intersect with other spheres           
            isValid = true;
            
            var minRadius = MaxRadius;
            
            foreach(var sphere in spheres)
            {
                if (sphere.Radius < 0.001f) break; // sphere wasn't set

                var validRadius = Vector3.Distance(sphere.Position, position) - sphere.Radius - Spacing;
                validRadius = Mathf.Min(validRadius, MaxRadius); // limit radius
                
                if (validRadius < MinRadius)
                {
                    // too close
                    isValid = false;
                    break;
                }

                // remember the smallest valid radius
                minRadius = Mathf.Min(validRadius, minRadius);
            }

            if (isValid)
            {
                radius = minRadius;
                position.y = radius;
            }
        }

        return isValid;
    }

    private Vector3 GenerateRandomColor()
    {
        var color = Color.HSVToRGB(GetRandomRange(0, 1), GetRandomRange(0.4f, 0.8f), GetRandomRange(0.75f, 1));
        return new Vector3(color.r, color.g, color.b);
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        Initialize();
        SetShaderParameters();
        RenderScene(dest);
        DisposeBuffers();
    }

    private void Initialize()
    {
        _camera = Camera.current;
        if (_shaderMaterial == null) _shaderMaterial = new Material(Shader.Find("Hidden/AddShader"));
    }

    private void DisposeBuffers()
    {
        for (int i = 0; i < _buffersToDispose.Count; i++)
        {
            _buffersToDispose[i].Release();
        }
        _buffersToDispose.Clear();
    }

    private void SetShaderParameters()
    {
        ScreenShader.SetMatrix("CameraToWorld", _camera.cameraToWorldMatrix);
        ScreenShader.SetMatrix("CameraInverseProjection", _camera.projectionMatrix.inverse);
        ScreenShader.SetTexture(0, "SkyTexture", SkyTexture);
        ScreenShader.SetVector("PixelOffset", new Vector4(NextFloat() - 0.5f, NextFloat() - 0.5f));
        ScreenShader.SetInt("MaxBounce", MaxBounce);
        ScreenShader.SetVector("GroundColor", GroundColor);
        ScreenShader.SetFloat("Seed", NextFloat());
        ScreenShader.SetFloat("SkyIntensity", SkyIntensity);

        var lightForward = DirectionalLight.transform.forward;
        ScreenShader.SetVector("DirectionalLight", new Vector4(lightForward.x, lightForward.y, lightForward.z, DirectionalLight.intensity));
        
        var buffer = new ComputeBuffer(_spheres.Length, Sphere.GetSize());
        buffer.SetData(_spheres);
        ScreenShader.SetBuffer(0, "Spheres", buffer);
        _buffersToDispose.Add(buffer);
        
        _shaderMaterial.SetFloat("_Sample", _sample);
        _sample++;
    }

    private void RenderScene(RenderTexture dest)
    {
        InitRenderTexture();
        
        ScreenShader.SetTexture(0, "Result", _screenTexture);

        int threadGroupsX = Mathf.CeilToInt(Screen.width / 8f);
        int threadGroupsY = Mathf.CeilToInt(Screen.height / 8f);
        
        ScreenShader.Dispatch(0, threadGroupsX, threadGroupsY, 1);
        
        Graphics.Blit(_screenTexture, _convergedTexture, _shaderMaterial);
        Graphics.Blit(_convergedTexture, dest);
    }

    private void InitRenderTexture()
    {
        if (_screenTexture != null && _screenTexture.width == Screen.width &&
            _screenTexture.height == Screen.height) return;

        if (_screenTexture != null)
        {
            _screenTexture.Release();
        }
        
        _screenTexture = new RenderTexture(Screen.width, Screen.height, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        _screenTexture.enableRandomWrite = true;
        
        _screenTexture.Create();

        if (_convergedTexture != null)
        {
            _convergedTexture.Release();
        }
        
        _convergedTexture = new RenderTexture(Screen.width, Screen.height, 32, RenderTextureFormat.ARGB64, RenderTextureReadWrite.Linear);
        _convergedTexture.enableRandomWrite = true;

        _convergedTexture.Create();
    }

    private void OnDrawGizmos()
    {
        /*if (_spheres == null) return;

        foreach (var sphere in _spheres)
        {
            Gizmos.color = new Color(sphere.Color.x, sphere.Color.y, sphere.Color.z, 0.5f);
            Gizmos.DrawSphere(sphere.Position, sphere.Radius);
        }*/
    }

    private float NextFloat()
    {
        return (float) _random.NextDouble();
    }
    
    private float GetRandomRange(float min, float max)
    {
        return (float) (_random.NextDouble() * (max - min) + min);
    }
}
