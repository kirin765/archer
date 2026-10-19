using UnityEngine;

namespace Archer
{
    /// <summary>Fixed-size spark pool. No GameObject or material creation during combat.</summary>
    public class CombatEffects : MonoBehaviour
    {
        struct Spark { public Transform Transform; public Renderer Renderer; public Vector3 Velocity; public float Life, Duration, Size; }
        readonly Spark[] _sparks=new Spark[64];
        MaterialPropertyBlock _properties;
        Material _material;
        int _cursor;
        public static CombatEffects Instance { get; private set; }

        void Awake()
        {
            Instance=this;
            _properties=new MaterialPropertyBlock();
            _material=Prim.Unlit(Color.white);
            for(int i=0;i<_sparks.Length;i++)
            {
                var go=Prim.Create(PrimitiveType.Cube,"Impact mote",transform,Vector3.zero,Vector3.one,_material,false);
                var renderer=go.GetComponent<Renderer>();
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                go.SetActive(false);
                _sparks[i]=new Spark { Transform=go.transform,Renderer=renderer };
            }
        }

        public void Burst(Vector3 position, bool blocked, bool lethal)
        {
            int count=lethal ? 16 : 7;
            Color color=blocked ? new Color(0.67f,0.83f,0.96f) : new Color(1f,0.74f,0.28f);
            for(int i=0;i<count;i++)
            {
                int index=_cursor++ % _sparks.Length;
                var spark=_sparks[index];
                spark.Life=spark.Duration=Random.Range(0.22f,0.48f);
                spark.Size=Random.Range(0.035f,0.075f)*(lethal?1.4f:1f);
                spark.Velocity=Random.onUnitSphere*Random.Range(1f,3f)+Vector3.up*1.2f;
                spark.Transform.position=position;
                spark.Transform.localScale=Vector3.one*spark.Size;
                spark.Transform.gameObject.SetActive(true);
                _properties.SetColor("_BaseColor",color); _properties.SetColor("_Color",color);
                spark.Renderer.SetPropertyBlock(_properties);
                _sparks[index]=spark;
            }
        }

        void Update()
        {
            for(int i=0;i<_sparks.Length;i++)
            {
                var spark=_sparks[i]; if(spark.Life<=0) continue;
                spark.Life-=Time.deltaTime;
                spark.Velocity+=Vector3.down*(3f*Time.deltaTime);
                spark.Transform.position+=spark.Velocity*Time.deltaTime;
                spark.Transform.localScale=Vector3.one*(spark.Size*Mathf.Clamp01(spark.Life/spark.Duration));
                if(spark.Life<=0) spark.Transform.gameObject.SetActive(false);
                _sparks[i]=spark;
            }
        }

        public void Clear()
        {
            for(int i=0;i<_sparks.Length;i++) { _sparks[i].Life=0; _sparks[i].Transform.gameObject.SetActive(false); }
        }

        void OnDestroy() { if(Instance==this) Instance=null; if(_material!=null) Destroy(_material); }
    }
}
