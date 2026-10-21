using System;
using System.Collections.Generic;
using UnityEngine;

namespace Archer.EditorTools
{
    /// <summary>
    /// Quadric-error-metric edge-collapse simplifier for skinned meshes.
    ///
    /// The imported goblin is ~1.4M triangles. The previous tooling clustered
    /// vertices on a spatial grid, which kept the triangle count down but
    /// threw away the UV layout (every cluster inherited one arbitrary
    /// vertex' texel) and dropped the tangents, so the normal map never
    /// reached the screen and the skin/cloth boundaries drifted.
    ///
    /// This decimator collapses edges instead:
    ///   - quadrics are area weighted, so flat regions collapse aggressively
    ///     and creases survive;
    ///   - boundary edges get a perpendicular constraint plane, so the
    ///     silhouette does not shrink or tear;
    ///   - texture error is folded into the cost, so UV seams are only
    ///     collapsed when nothing else is left;
    ///   - bone weights are merged and renormalised to the requested number of
    ///     influences, which halves the skinning cost.
    /// </summary>
    public static class GoblinMeshSimplifier
    {
        public struct Settings
        {
            public int TargetTriangles;
            /// <summary>How hard a collapsed UV seam is penalised.</summary>
            public float TextureWeight;
            /// <summary>How hard a collapsed silhouette edge is penalised.</summary>
            public float BoundaryWeight;
            /// <summary>Bone influences kept per vertex (1-4).</summary>
            public int MaxBoneInfluences;

            public static Settings For(int targetTriangles) => new Settings
            {
                TargetTriangles = targetTriangles,
                TextureWeight = 1.2f,
                BoundaryWeight = 90f,
                MaxBoneInfluences = 2
            };
        }

        public static Mesh Simplify(Mesh source, Settings settings)
        {
            return new Work(source, settings).Run();
        }

        const int QStride = 10;   // symmetric 4x3 quadric, 10 unique entries

        sealed class Work
        {
            readonly Mesh _source;
            readonly Settings _settings;

            Vector3[] _pos;
            Vector3[] _nrm;
            Vector2[] _uv;
            BoneWeight[] _bone;
            int[] _tri;
            bool[] _faceAlive;
            int _aliveFaces;

            // half-edge adjacency: h = face*3 + corner, vertex is _tri[h]
            int[] _faceHead;
            int[] _faceNext;

            bool[] _dead;
            float[] _quad;      // QStride floats per vertex
            float _textureScale;

            // min-heap of collapse candidates
            Item[] _heap;
            int _heapCount;

            struct Item
            {
                public float Cost;
                public int A;
                public int B;
                public byte Retries;
            }

            public Work(Mesh source, Settings settings)
            {
                _source = source;
                _settings = settings;
            }

            public Mesh Run()
            {
                int vCount = _source.vertexCount;
                _pos = _source.vertices;
                _nrm = _source.normals;
                _uv = _source.uv;
                _bone = _source.boneWeights;
                _tri = _source.triangles;

                if (_nrm == null || _nrm.Length != vCount) _nrm = new Vector3[vCount];
                if (_uv == null || _uv.Length != vCount) _uv = new Vector2[vCount];
                if (_bone == null || _bone.Length != vCount) _bone = new BoneWeight[vCount];

                int fCount = _tri.Length / 3;
                _faceAlive = new bool[fCount];
                _aliveFaces = fCount;
                for (int i = 0; i < fCount; i++) _faceAlive[i] = true;

                BuildAdjacency(vCount);
                BuildQuadrics(vCount, fCount);
                BuildEdgeSet(vCount, fCount);

                _heap = new Item[Mathf.Max(1024, fCount)];
                SeedHeap(fCount);

                while (_aliveFaces > _settings.TargetTriangles && _heapCount > 0)
                {
                    var item = Pop();
                    if (_dead[item.A] || _dead[item.B]) continue;
                    if (!StillAdjacent(item.A, item.B)) continue;

                    float cost = CollapseCost(item.A, item.B, out Vector3 target);
                    if (cost > item.Cost * 1.05f + 1e-9f && item.Retries < 4)
                    {
                        item.Cost = cost;
                        item.Retries++;
                        Push(item);
                        continue;
                    }
                    Collapse(item.A, item.B, target);
                }

                return Build();
            }

            // ---------------------------------------------------------------- adjacency

            void BuildAdjacency(int vCount)
            {
                _faceHead = new int[vCount];
                for (int i = 0; i < vCount; i++) _faceHead[i] = -1;
                _faceNext = new int[_tri.Length];
                for (int f = 0; f < _aliveFaces; f++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int h = f * 3 + k;
                        int v = _tri[h];
                        _faceNext[h] = _faceHead[v];
                        _faceHead[v] = h;
                    }
                }
                _dead = new bool[vCount];
            }

            // ---------------------------------------------------------------- quadrics

            void BuildQuadrics(int vCount, int fCount)
            {
                _quad = new float[vCount * QStride];
                for (int f = 0; f < fCount; f++)
                {
                    int o = f * 3;
                    Vector3 a = _pos[_tri[o]];
                    Vector3 b = _pos[_tri[o + 1]];
                    Vector3 c = _pos[_tri[o + 2]];
                    Vector3 n = Vector3.Cross(b - a, c - a);
                    float len = n.magnitude;
                    if (len < 1e-12f) continue;
                    float area = len * 0.5f;
                    n /= len;
                    float d = -Vector3.Dot(n, a);
                    AddQuadric(_tri[o], n.x, n.y, n.z, d, area);
                    AddQuadric(_tri[o + 1], n.x, n.y, n.z, d, area);
                    AddQuadric(_tri[o + 2], n.x, n.y, n.z, d, area);
                }
            }

            void AddQuadric(int v, float a, float b, float c, float d, float w)
            {
                int o = v * QStride;
                _quad[o + 0] += w * a * a;
                _quad[o + 1] += w * a * b;
                _quad[o + 2] += w * a * c;
                _quad[o + 3] += w * a * d;
                _quad[o + 4] += w * b * b;
                _quad[o + 5] += w * b * c;
                _quad[o + 6] += w * b * d;
                _quad[o + 7] += w * c * c;
                _quad[o + 8] += w * c * d;
                _quad[o + 9] += w * d * d;
            }

            float QuadError(int v, Vector3 p)
            {
                int o = v * QStride;
                float x = p.x, y = p.y, z = p.z;
                return _quad[o + 0] * x * x + 2f * _quad[o + 1] * x * y + 2f * _quad[o + 2] * x * z + 2f * _quad[o + 3] * x
                     + _quad[o + 4] * y * y + 2f * _quad[o + 5] * y * z + 2f * _quad[o + 6] * y
                     + _quad[o + 7] * z * z + 2f * _quad[o + 8] * z + _quad[o + 9];
            }

            /// <summary>Vertex that minimises the merged quadric, clamped to the edge.</summary>
            Vector3 OptimalPosition(int a, int b, out bool valid)
            {
                valid = false;
                int oa = a * QStride, ob = b * QStride;
                float m0 = _quad[oa + 0] + _quad[ob + 0];
                float m1 = _quad[oa + 1] + _quad[ob + 1];
                float m2 = _quad[oa + 2] + _quad[ob + 2];
                float m3 = _quad[oa + 3] + _quad[ob + 3];
                float m4 = _quad[oa + 4] + _quad[ob + 4];
                float m5 = _quad[oa + 5] + _quad[ob + 5];
                float m6 = _quad[oa + 6] + _quad[ob + 6];
                float m7 = _quad[oa + 7] + _quad[ob + 7];
                float m8 = _quad[oa + 8] + _quad[ob + 8];
                float m9 = _quad[oa + 9] + _quad[ob + 9];

                float det = m0 * (m4 * m7 - m5 * m5) - m1 * (m1 * m7 - m5 * m2) + m2 * (m1 * m5 - m4 * m2);
                if (Mathf.Abs(det) < 1e-12f) return Vector3.Lerp(_pos[a], _pos[b], 0.5f);

                // Adjugate of the symmetric 3x3 system, solved against -[m3,m6,m8].
                float a00 = m4 * m7 - m5 * m5;
                float a01 = m2 * m5 - m1 * m7;
                float a02 = m1 * m5 - m2 * m4;
                float a11 = m0 * m7 - m2 * m2;
                float a12 = m1 * m2 - m0 * m5;
                float a22 = m0 * m4 - m1 * m1;

                float inv = 1f / det;
                float x = -(a00 * m3 + a01 * m6 + a02 * m8) * inv;
                float y = -(a01 * m3 + a11 * m6 + a12 * m8) * inv;
                float z = -(a02 * m3 + a12 * m6 + a22 * m8) * inv;
                if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) return Vector3.Lerp(_pos[a], _pos[b], 0.5f);
                valid = true;
                return new Vector3(x, y, z);
            }

            // ---------------------------------------------------------------- edges

            /// <summary>
            /// Builds the unique edge list, finds silhouette (boundary) edges and
            /// measures how many world units one texture unit covers.
            /// </summary>
            void BuildEdgeSet(int vCount, int fCount)
            {
                int total = fCount * 3;
                var keys = new long[total];
                int n = 0;
                for (int f = 0; f < fCount; f++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int a = _tri[f * 3 + k];
                        int b = _tri[f * 3 + (k + 1) % 3];
                        keys[n++] = (long)Mathf.Min(a, b) * vCount + Mathf.Max(a, b);
                    }
                }
                Array.Sort(keys);

                // Boundary edges appear once; interior edges twice.
                double worldSum = 0, uvSum = 0;
                var boundary = new List<int>();
                int i = 0;
                while (i < total)
                {
                    int j = i + 1;
                    while (j < total && keys[j] == keys[i]) j++;
                    if (j - i == 1)
                    {
                        int v0 = (int)(keys[i] / vCount);
                        int v1 = (int)(keys[i] % vCount);
                        boundary.Add(v0);
                        boundary.Add(v1);
                    }
                    else
                    {
                        int v0 = (int)(keys[i] / vCount);
                        int v1 = (int)(keys[i] % vCount);
                        float duv = (_uv[v0] - _uv[v1]).magnitude;
                        float dp = (_pos[v0] - _pos[v1]).magnitude;
                        if (duv > 1e-4f && dp > 1e-5f) { worldSum += dp; uvSum += duv; }
                    }
                    i = j;
                }
                _textureScale = uvSum > 1e-6f ? (float)(worldSum / uvSum) : 1f;

                // Constrain the silhouette: a plane through the boundary edge and
                // perpendicular to its face keeps the outline from folding in.
                for (int b = 0; b < boundary.Count; b += 2)
                {
                    int v0 = boundary[b];
                    int v1 = boundary[b + 1];
                    int face = FindFaceForEdge(v0, v1);
                    if (face < 0) continue;
                    Vector3 a = _pos[_tri[face * 3]];
                    Vector3 c = _pos[_tri[face * 3 + 1]];
                    Vector3 d = _pos[_tri[face * 3 + 2]];
                    Vector3 fn = Vector3.Cross(c - a, d - a).normalized;
                    Vector3 edgeDir = (_pos[v1] - _pos[v0]);
                    if (edgeDir.sqrMagnitude < 1e-12f) continue;
                    edgeDir.Normalize();
                    Vector3 cn = Vector3.Cross(edgeDir, fn);
                    if (cn.sqrMagnitude < 1e-12f) continue;
                    cn.Normalize();
                    float plane = -Vector3.Dot(cn, _pos[v0]);
                    AddQuadric(v0, cn.x, cn.y, cn.z, plane, _settings.BoundaryWeight);
                    AddQuadric(v1, cn.x, cn.y, cn.z, plane, _settings.BoundaryWeight);
                }
            }

            int FindFaceForEdge(int v0, int v1)
            {
                for (int h = _faceHead[v0]; h != -1; h = _faceNext[h])
                {
                    int f = h / 3;
                    if (!_faceAlive[f]) continue;
                    int o = f * 3;
                    if (_tri[o] == v1 || _tri[o + 1] == v1 || _tri[o + 2] == v1) return f;
                }
                return -1;
            }

            // ---------------------------------------------------------------- heap

            void SeedHeap(int fCount)
            {
                for (int f = 0; f < fCount; f++)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int a = _tri[f * 3 + k];
                        int b = _tri[f * 3 + (k + 1) % 3];
                        if (a == b) continue;
                        float costA = CollapseCost(a, b, out Vector3 targetA);
                        float costB = CollapseCost(b, a, out Vector3 targetB);
                        if (costA <= costB) Push(new Item { Cost = costA, A = a, B = b, Retries = 0 });
                        else Push(new Item { Cost = costB, A = b, B = a, Retries = 0 });
                    }
                }
            }

            void Push(Item item)
            {
                if (_heapCount == _heap.Length)
                {
                    Array.Resize(ref _heap, Mathf.Max(1024, _heap.Length * 2));
                }
                int i = _heapCount++;
                _heap[i] = item;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_heap[parent].Cost <= _heap[i].Cost) break;
                    (_heap[parent], _heap[i]) = (_heap[i], _heap[parent]);
                    i = parent;
                }
            }

            Item Pop()
            {
                Item top = _heap[0];
                _heapCount--;
                if (_heapCount > 0)
                {
                    _heap[0] = _heap[_heapCount];
                    int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1, r = l + 1, s = i;
                        if (l < _heapCount && _heap[l].Cost < _heap[s].Cost) s = l;
                        if (r < _heapCount && _heap[r].Cost < _heap[s].Cost) s = r;
                        if (s == i) break;
                        (_heap[s], _heap[i]) = (_heap[i], _heap[s]);
                        i = s;
                    }
                }
                return top;
            }

            // ---------------------------------------------------------------- cost

            float CollapseCost(int a, int b, out Vector3 target)
            {
                Vector3 p = OptimalPosition(a, b, out bool valid);
                Vector3 pa = _pos[a], pb = _pos[b];
                Vector3 dir = pb - pa;
                float len2 = dir.sqrMagnitude;
                if (valid && len2 > 1e-12f)
                {
                    float t = Mathf.Clamp01(Vector3.Dot(p - pa, dir) / len2);
                    p = pa + dir * t;
                }
                else p = Vector3.Lerp(pa, pb, 0.5f);

                float err = QuadError(a, p) + QuadError(b, p);

                // Texture error: how far the texel footprint moves when the two
                // vertices are merged. UV seams (same place, different texel)
                // produce a large value and survive until nothing else is left.
                float duv = (_uv[a] - _uv[b]).magnitude;
                float texErr = duv * _textureScale;
                err += _settings.TextureWeight * texErr * texErr;

                target = p;
                return err;
            }

            // ---------------------------------------------------------------- collapse

            bool StillAdjacent(int a, int b)
            {
                for (int h = _faceHead[a]; h != -1; h = _faceNext[h])
                {
                    int f = h / 3;
                    if (!_faceAlive[f]) continue;
                    int o = f * 3;
                    if (_tri[o] == b || _tri[o + 1] == b || _tri[o + 2] == b) return true;
                }
                return false;
            }

            void Collapse(int v0, int v1, Vector3 target)
            {
                // Move the surviving half-edges from v0 to v1 and drop any face
                // that collapses onto itself.
                var moved = new System.Collections.Generic.List<int>(8);
                for (int h = _faceHead[v0]; h != -1;)
                {
                    int next = _faceNext[h];
                    int f = h / 3;
                    if (_faceAlive[f])
                    {
                        if (_tri[h] == v0) _tri[h] = v1;
                        int o = f * 3;
                        int a = _tri[o], b = _tri[o + 1], c = _tri[o + 2];
                        if (a == b || b == c || a == c)
                        {
                            _faceAlive[f] = false;
                            _aliveFaces--;
                        }
                        else moved.Add(h);
                    }
                    h = next;
                }
                foreach (int m in moved)
                {
                    _faceNext[m] = _faceHead[v1];
                    _faceHead[v1] = m;
                }
                _faceHead[v0] = -1;

                _pos[v1] = target;
                _nrm[v1] = (_nrm[v0] + _nrm[v1]).normalized;
                // The surviving vertex keeps its own texel; the texture error
                // term in the cost keeps that from tearing the layout.
                _bone[v1] = MergeBones(_bone[v0], _bone[v1], _settings.MaxBoneInfluences);
                for (int k = 0; k < QStride; k++) _quad[v1 * QStride + k] += _quad[v0 * QStride + k];
                _dead[v0] = true;

                // Re-price every edge that now touches the survivor.
                for (int h = _faceHead[v1]; h != -1; h = _faceNext[h])
                {
                    int f = h / 3;
                    if (!_faceAlive[f]) continue;
                    int o = f * 3;
                    for (int k = 0; k < 3; k++)
                    {
                        int n = _tri[o + k];
                        if (n == v1) continue;
                        if (!_dead[n])
                        {
                            float cost = CollapseCost(v1, n, out Vector3 t);
                            Push(new Item { Cost = cost, A = v1, B = n, Retries = 0 });
                        }
                    }
                }
            }

            static BoneWeight MergeBones(BoneWeight a, BoneWeight b, int maxInfluences)
            {
                var idx = new int[8];
                var wgt = new float[8];
                int count = 0;
                Add(a.boneIndex0, a.weight0); Add(a.boneIndex1, a.weight1);
                Add(a.boneIndex2, a.weight2); Add(a.boneIndex3, a.weight3);
                Add(b.boneIndex0, b.weight0); Add(b.boneIndex1, b.weight1);
                Add(b.boneIndex2, b.weight2); Add(b.boneIndex3, b.weight3);
                return Finish();

                void Add(int bone, float weight)
                {
                    if (weight <= 0f) return;
                    for (int i = 0; i < count; i++)
                        if (idx[i] == bone) { wgt[i] += weight; return; }
                    idx[count] = bone;
                    wgt[count] = weight;
                    count++;
                }

                BoneWeight Finish()
                {
                    // keep the heaviest influences
                    for (int i = 0; i < count; i++)
                    {
                        int best = i;
                        for (int j = i + 1; j < count; j++) if (wgt[j] > wgt[best]) best = j;
                        (idx[i], idx[best]) = (idx[best], idx[i]);
                        (wgt[i], wgt[best]) = (wgt[best], wgt[i]);
                    }
                    int keep = Mathf.Min(maxInfluences, count);
                    float total = 0f;
                    for (int i = 0; i < keep; i++) total += wgt[i];
                    var result = new BoneWeight();
                    if (total <= 0f)
                    {
                        result.boneIndex0 = 0; result.weight0 = 1f;
                        return result;
                    }
                    for (int i = 0; i < keep; i++)
                    {
                        float w = wgt[i] / total;
                        switch (i)
                        {
                            case 0: result.boneIndex0 = idx[i]; result.weight0 = w; break;
                            case 1: result.boneIndex1 = idx[i]; result.weight1 = w; break;
                            case 2: result.boneIndex2 = idx[i]; result.weight2 = w; break;
                            default: result.boneIndex3 = idx[i]; result.weight3 = w; break;
                        }
                    }
                    return result;
                }
            }

            // ---------------------------------------------------------------- output

            Mesh Build()
            {
                // Compact the survivors.
                var map = new int[_pos.Length];
                for (int i = 0; i < map.Length; i++) map[i] = -1;
                int vCount = 0;
                var vertices = new List<Vector3>(_aliveFaces);
                var normals = new List<Vector3>(_aliveFaces);
                var uvs = new List<Vector2>(_aliveFaces);
                var bones = new List<BoneWeight>(_aliveFaces);
                var triangles = new List<int>(_aliveFaces * 3);

                for (int f = 0; f < _faceAlive.Length; f++)
                {
                    if (!_faceAlive[f]) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = _tri[f * 3 + k];
                        if (map[v] < 0)
                        {
                            map[v] = vCount++;
                            vertices.Add(_pos[v]);
                            normals.Add(_nrm[v]);
                            uvs.Add(_uv[v]);
                            bones.Add(_bone[v]);
                        }
                        triangles.Add(map[v]);
                    }
                }

                var result = new Mesh
                {
                    name = _source.name,
                    indexFormat = vCount > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
                };
                result.SetVertices(vertices);
                result.SetNormals(normals);
                result.SetUVs(0, uvs);
                result.boneWeights = bones.ToArray();
                result.bindposes = _source.bindposes;
                result.SetTriangles(triangles, 0, true);
                // The source bake carries tangents; a decimated copy needs its
                // own so the normal map actually reaches the screen.
                result.RecalculateTangents();
                result.RecalculateBounds();
                return result;
            }
        }
    }
}
