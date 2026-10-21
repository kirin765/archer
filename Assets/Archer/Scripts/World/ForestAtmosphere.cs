using UnityEngine;

namespace Archer
{
    /// <summary>Owns the generated sky and small environmental motion.</summary>
    public class ForestAtmosphere : MonoBehaviour
    {
        static readonly Vector3 MoonDirection = new Vector3(80f, 150f, 400f);

        Material _sky, _previousSky;
        Light[] _lanterns;
        float[] _intensities;
        float _appliedNight = -1f;

        void Start()
        {
            _previousSky=RenderSettings.skybox;
            var shader=Resources.Load<Shader>("LastRangerSky");
            if(shader != null)
            {
                _sky=new Material(shader);
                _sky.SetVector("_MoonDirection",MoonDirection);
                RenderSettings.skybox=_sky;
                if(Camera.main != null) Camera.main.clearFlags=CameraClearFlags.Skybox;
            }
            var lights=GetComponentsInChildren<Light>();
            var list=new System.Collections.Generic.List<Light>();
            foreach(var light in lights) if(light.type==LightType.Point) list.Add(light);
            _lanterns=list.ToArray(); _intensities=new float[_lanterns.Length];
            for(int i=0;i<_lanterns.Length;i++) _intensities[i]=_lanterns[i].intensity;
        }

        void Update()
        {
            float night=TimeOfDay.Instance != null ? Mathf.InverseLerp(0.65f,0.79f,TimeOfDay.Instance.Time01) : 0;
            // The clock drifts slowly; only touch the sky material when the
            // night factor actually moved instead of every frame.
            if(_sky != null && Mathf.Abs(night-_appliedNight) > 0.0005f)
            {
                _appliedNight=night;
                _sky.SetColor("_Zenith",Color.Lerp(new Color(0.045f,0.13f,0.27f),new Color(0.018f,0.04f,0.12f),night));
                _sky.SetColor("_Horizon",Color.Lerp(new Color(0.48f,0.38f,0.34f),new Color(0.12f,0.22f,0.33f),night));
                _sky.SetColor("_CloudTint",Color.Lerp(new Color(0.80f,0.55f,0.46f),new Color(0.42f,0.50f,0.68f),night));
                _sky.SetFloat("_Night",night);
            }
            for(int i=0;_lanterns!=null && i<_lanterns.Length;i++)
                if(_lanterns[i]!=null) _lanterns[i].intensity=_intensities[i]*(1f+0.045f*Mathf.Sin(Time.time*3.1f+i*2.3f));
        }

        void OnDestroy()
        {
            if(RenderSettings.skybox==_sky) RenderSettings.skybox=_previousSky;
            if(_sky!=null) Destroy(_sky);
        }
    }
}
