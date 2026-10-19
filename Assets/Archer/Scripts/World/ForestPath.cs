using UnityEngine;

namespace Archer
{
    public class ForestPath : MonoBehaviour
    {
        Texture2D _texture;
        Mesh _mesh;

        public static void Build(Transform parent, float length)
        {
            var go=new GameObject("Worn forest path",typeof(MeshFilter),typeof(MeshRenderer));
            go.transform.SetParent(parent,false);
            var path=go.AddComponent<ForestPath>();
            path.Create(length);
        }

        void Create(float length)
        {
            const int segments=40;
            var vertices=new Vector3[(segments+1)*2];
            var uv=new Vector2[vertices.Length];
            var triangles=new int[segments*6];
            for(int i=0;i<=segments;i++)
            {
                float z=Mathf.Lerp(-12f,length-12f,(float)i/segments);
                float center=Mathf.Sin(z*0.055f)*0.7f;
                float width=5f+Mathf.PerlinNoise(i*0.4f,4f)*0.65f;
                vertices[i*2]=new Vector3(center-width,0.035f,z);
                vertices[i*2+1]=new Vector3(center+width,0.035f,z);
                uv[i*2]=new Vector2(0,z*0.3f); uv[i*2+1]=new Vector2(width*0.6f,z*0.3f);
                if(i==segments) continue;
                int v=i*2,t=i*6;
                triangles[t]=v;triangles[t+1]=v+2;triangles[t+2]=v+1;
                triangles[t+3]=v+1;triangles[t+4]=v+2;triangles[t+5]=v+3;
            }
            _mesh=new Mesh { name="Forest path",vertices=vertices,uv=uv,triangles=triangles };
            _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh=_mesh;
            _texture=new Texture2D(128,128,TextureFormat.RGB24,true) { name="Procedural earth",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Bilinear };
            var colors=new Color[128*128]; var random=new System.Random(73);
            for(int y=0;y<128;y++) for(int x=0;x<128;x++)
            {
                float grain=(float)random.NextDouble();
                float shade=0.87f+grain*0.16f+Mathf.PerlinNoise(x*0.08f,y*0.08f)*0.12f;
                colors[y*128+x]=new Color(0.40f,0.32f,0.23f)*shade;
            }
            _texture.SetPixels(colors); _texture.Apply();
            var material=Prim.Material(Color.white,0.04f); material.mainTexture=_texture;
            GetComponent<MeshRenderer>().sharedMaterial=material;
        }

        void OnDestroy() { if(_mesh!=null) Destroy(_mesh); if(_texture!=null) Destroy(_texture); }
    }
}
