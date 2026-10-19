using UnityEngine;

namespace Archer
{
    /// <summary>Authored, repeatable low-poly scenery, kept outside the shooting lane.</summary>
    public static class ForestScene
    {
        // One unit cone per side count, reused by every tree tier. Building a
        // unique mesh per cone wasted memory and defeated batching.
        static readonly System.Collections.Generic.Dictionary<int, Mesh> ConeMeshes = new();

        public static GameObject Cone(string name, Transform parent, Vector3 position, Vector3 scale, Material material, int sides = 7)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = scale;
            if (!ConeMeshes.TryGetValue(sides, out var mesh))
            {
                var vertices = new Vector3[sides * 6];
                var triangles = new int[vertices.Length];
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides, b = (i+1)*Mathf.PI*2f/sides;
                    int v=i*6;
                    vertices[v] = new Vector3(Mathf.Cos(a)*0.5f, 0, Mathf.Sin(a)*0.5f);
                    vertices[v+1] = new Vector3(0,1,0);
                    vertices[v+2] = new Vector3(Mathf.Cos(b)*0.5f, 0, Mathf.Sin(b)*0.5f);
                    vertices[v+3] = Vector3.zero; vertices[v+4] = vertices[v]; vertices[v+5] = vertices[v+2];
                    for(int j=0;j<6;j++) triangles[v+j]=v+j;
                }
                mesh = new Mesh { name = name+" mesh", vertices = vertices, triangles = triangles };
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                ConeMeshes[sides] = mesh;
            }
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        public static void Build(Transform world)
        {
            var root = new GameObject("Forest silhouettes").transform; root.SetParent(world, false);
            var trunk = Prim.Material(new Color(0.22f,0.15f,0.11f));
            var leaves = new[] { Prim.Material(new Color(0.10f,0.24f,0.23f)), Prim.Material(new Color(0.13f,0.30f,0.27f)), Prim.Material(new Color(0.17f,0.34f,0.29f)) };
            var rocks = Prim.Material(new Color(0.29f,0.35f,0.39f));
            var grass = Prim.Material(new Color(0.36f,0.40f,0.22f));
            var random = new System.Random(481);
            float Next(float min,float max) => Mathf.Lerp(min,max,(float)random.NextDouble());
            for(int side=-1;side<=1;side+=2)
            {
                for(int i=0;i<28;i++)
                {
                    float z=2+i*3.9f+Next(-1,1), x=side*Next(8.2f,17f), h=Next(5f,10f);
                    var tree = new GameObject("Pine").transform; tree.SetParent(root,false); tree.localPosition=new Vector3(x,0,z);
                    Prim.Create(PrimitiveType.Cylinder,"Trunk",tree,new Vector3(0,h*0.25f,0),new Vector3(0.4f,h*0.25f,0.4f),trunk,false);
                    for(int tier=0;tier<3;tier++)
                        Cone("Pine canopy",tree,new Vector3(0,h*(0.24f+tier*0.2f),0),new Vector3(h*(0.65f-tier*0.15f),h*0.5f,h*(0.65f-tier*0.15f)),leaves[(i+tier)%leaves.Length]);
                }
                for(int i=0;i<30;i++)
                {
                    float z=Next(1,85),x=side*Next(6f,9f);
                    var rock=Prim.Create(PrimitiveType.Cube,"Path rock",root,new Vector3(x,0.25f,z),new Vector3(Next(0.4f,1.2f),Next(0.35f,0.8f),Next(0.4f,1.3f)),rocks,false);
                    rock.transform.localRotation=Quaternion.Euler(Next(-15,15),Next(0,180),Next(-15,15));
                    if(i%2==0) Cone("Grass tuft",root,new Vector3(x*0.95f,0,z+1),new Vector3(0.5f,0.65f,0.45f),grass,4);
                }
                for(int i=0;i<5;i++)
                    Cone("Distant ridge",root,new Vector3(side*(12+i*9),-1,100+i*8),new Vector3(35,Next(15,30),35),leaves[0],5);
            }

            // Collapse ~320 decorative renderers into one batch per material.
            StaticBatchingUtility.Combine(root.gameObject);
        }
    }
}
