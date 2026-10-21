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
            // Runtime meshes do not survive a play-mode exit when domain reload
            // is disabled, while this static cache does. Rebuild a stale entry
            // instead of assigning a destroyed mesh (which renders no canopy).
            if (!ConeMeshes.TryGetValue(sides, out var mesh) || mesh == null)
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
            var grass = new[] { Prim.Material(new Color(0.36f,0.40f,0.22f)), Prim.Material(new Color(0.28f,0.35f,0.20f)) };
            var fern = Prim.Material(new Color(0.14f,0.30f,0.20f));
            // Ridge layers fade toward the dusk haze so distance reads as depth.
            var haze = new Color(0.16f,0.25f,0.38f);
            var ridges = new[]
            {
                Prim.Material(Color.Lerp(leaves[0].color, haze, 0.30f)),
                Prim.Material(Color.Lerp(leaves[0].color, haze, 0.55f)),
                Prim.Material(Color.Lerp(leaves[0].color, haze, 0.80f)),
            };
            var random = new System.Random(481);
            float Next(float min,float max) => Mathf.Lerp(min,max,(float)random.NextDouble());
            for(int side=-1;side<=1;side+=2)
            {
                // Pines: varied height, tier count, spacing and proportions so
                // the rows no longer read as copies of one tree.
                for(int i=0;i<28;i++)
                {
                    float z=2+i*3.9f+Next(-1.4f,1.4f), x=side*Next(8.2f,18f), h=Next(4.5f,11.5f);
                    var tree = new GameObject("Pine").transform; tree.SetParent(root,false); tree.localPosition=new Vector3(x,0,z);
                    tree.localRotation=Quaternion.Euler(Next(-2f,2f),Next(0f,360f),Next(-2.5f,2.5f));
                    float trunkWidth=Next(0.32f,0.5f);
                    Prim.Create(PrimitiveType.Cylinder,"Trunk",tree,new Vector3(0,h*0.25f,0),new Vector3(trunkWidth,h*0.25f,trunkWidth),trunk,false);
                    int tiers=random.NextDouble()<0.7?3:(random.NextDouble()<0.5?2:4);
                    float tierStep=Next(0.16f,0.22f), baseWidth=Next(0.55f,0.72f), tierHeight=Next(0.42f,0.54f);
                    for(int tier=0;tier<tiers;tier++)
                    {
                        float w=h*Mathf.Max(0.18f,baseWidth-tier*Next(0.12f,0.16f));
                        Cone("Pine canopy",tree,new Vector3(0,h*(0.26f+tier*tierStep),0),new Vector3(w,h*tierHeight,w),leaves[(i+tier)%leaves.Length]);
                    }
                }
                // Rocks and grass clumps along the lane edge.
                for(int i=0;i<30;i++)
                {
                    float z=Next(1,85),x=side*Next(6.4f,9f);
                    var rock=Prim.Create(PrimitiveType.Cube,"Path rock",root,new Vector3(x,0.25f,z),new Vector3(Next(0.4f,1.2f),Next(0.35f,0.8f),Next(0.4f,1.3f)),rocks,false);
                    rock.transform.localRotation=Quaternion.Euler(Next(-15,15),Next(0,180),Next(-15,15));
                    int tufts=(int)Next(1,3.4f);
                    for(int t=0;t<tufts;t++)
                        Cone("Grass tuft",root,new Vector3(x*Next(0.9f,1.05f)+Next(-1,1),0,z+Next(-1,2)),new Vector3(Next(0.35f,0.7f),Next(0.4f,0.8f),Next(0.35f,0.6f)),grass[(i+t)%grass.Length],4);
                }
                // Fern clusters: several flattened cones fanned around a clump.
                for(int i=0;i<14;i++)
                {
                    float z=Next(3,80),x=side*Next(6.8f,12f);
                    int fronds=(int)Next(4,7);
                    for(int f=0;f<fronds;f++)
                    {
                        float angle=f*(360f/fronds)+Next(-18,18);
                        var frond=Cone("Fern",root,new Vector3(x+Next(-0.2f,0.2f),0,z+Next(-0.2f,0.2f)),new Vector3(Next(0.5f,0.9f),Next(0.35f,0.6f),Next(0.28f,0.45f)),fern,5);
                        frond.transform.localRotation=Quaternion.Euler(Next(6,16),angle,0);
                    }
                }
                // Occasional fallen branches.
                for(int i=0;i<6;i++)
                {
                    float z=Next(6,78),x=side*Next(6.6f,10f);
                    float len=Next(1.2f,2.4f);
                    var branch=Prim.Create(PrimitiveType.Cylinder,"Fallen branch",root,new Vector3(x,0.07f,z),new Vector3(0.09f,len*0.5f,0.09f),trunk,false);
                    branch.transform.localRotation=Quaternion.Euler(90+Next(-5,5),Next(0,180),0);
                    if(random.NextDouble()<0.5)
                    {
                        var stub=Prim.Create(PrimitiveType.Cylinder,"Branch stub",root,new Vector3(x+Next(-0.4f,0.4f),0.12f,z+Next(-0.4f,0.4f)),new Vector3(0.06f,0.28f,0.06f),trunk,false);
                        stub.transform.localRotation=Quaternion.Euler(Next(20,50),Next(0,180),0);
                    }
                }
                // Layered distant ridges: nearer darker, farther hazier.
                for(int layer=0;layer<3;layer++)
                    for(int i=0;i<3;i++)
                        Cone("Distant ridge",root,new Vector3(side*(10+i*13+layer*5)+Next(-3,3),-1,88+layer*14+i*9),new Vector3(Next(26,38),Next(13,26)+layer*4,Next(26,38)),ridges[layer],5);
            }

            // Collapse the decorative renderers into one mesh per material per z-section.
            SceneryBatcher.Combine(root.gameObject, 40f);
        }
    }
}
