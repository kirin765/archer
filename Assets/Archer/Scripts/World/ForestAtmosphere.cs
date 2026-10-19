using UnityEngine;

namespace Archer
{
    /// <summary>Owns the generated sky and small environmental motion.</summary>
    public class ForestAtmosphere : MonoBehaviour
    {
        Material _sky, _previousSky;
        Light[] _lanterns;
        float[] _intensities;

        void Start()
        {
            _previousSky=RenderSettings.skybox;
            var shader=Resources.Load<Shader>("LastRangerSky");
            if(shader != null)
            {
                _sky=new Material(shader);
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
            if(_sky != null)
            {
                _sky.SetColor("_Zenith",Color.Lerp(new Color(0.045f,0.13f,0.27f),new Color(0.018f,0.04f,0.12f),night));
                _sky.SetColor("_Horizon",Color.Lerp(new Color(0.48f,0.38f,0.34f),new Color(0.12f,0.22f,0.33f),night));
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
