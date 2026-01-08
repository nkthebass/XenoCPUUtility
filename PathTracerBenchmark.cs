using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using System.Drawing;

namespace CPUUtilityHybrid
{
    public class PathTracerBenchmark
    {
        private const int MAX_BOUNCES = 6;
        private const float RAY_EPSILON = 0.001f;

        // Material types
        public enum MaterialType { Diffuse, Metal, Glass }

        // Simple scene primitives
        public struct Sphere
        {
            public Vector3 Center;
            public float Radius;
            public Vector3 Color;
            public bool IsLight;
            public float Emission;
            public MaterialType Material;
            public float Roughness;  // 0 = perfect, 1 = diffuse
            public float IOR;        // Index of refraction for glass
        }

        public struct Plane
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector3 Color;
        }

        public struct Ray
        {
            public Vector3 Origin;
            public Vector3 Direction;
        }

        public struct HitRecord
        {
            public bool Hit;
            public float Distance;
            public Vector3 Point;
            public Vector3 Normal;
            public Vector3 Color;
            public bool IsLight;
            public float Emission;
            public MaterialType Material;
            public float Roughness;
            public float IOR;
        }

        // BVH Node
        private struct BVHNode
        {
            public int SphereIndex;
            public bool IsLeaf;
            public Vector3 BoundsMin;
            public Vector3 BoundsMax;
            public int LeftChild;
            public int RightChild;
        }

        private List<Sphere> spheres;
        private List<Plane> planes;
        private List<BVHNode> bvhNodes;
        private Random random = new Random(42);  // Seeded for consistency

        public PathTracerBenchmark()
        {
            InitializeScene();
        }

        private void InitializeScene()
        {
            spheres = new List<Sphere>
            {
                // Main light sphere
                new Sphere { Center = new Vector3(0, 3.5f, 1), Radius = 0.8f, Color = Vector3.One, IsLight = true, Emission = 2.5f, Material = MaterialType.Diffuse, Roughness = 1, IOR = 1 },
                // Secondary light (warmer)
                new Sphere { Center = new Vector3(-2, 2.5f, 1), Radius = 0.3f, Color = new Vector3(1, 0.9f, 0.7f), IsLight = true, Emission = 1.0f, Material = MaterialType.Diffuse, Roughness = 1, IOR = 1 },
                
                // Large glass sphere (center) - crystal clear
                new Sphere { Center = new Vector3(0, 1.2f, 2.5f), Radius = 1.0f, Color = Vector3.One, IsLight = false, Emission = 0, Material = MaterialType.Glass, Roughness = 0, IOR = 1.5f },
                
                // Chrome metal sphere
                new Sphere { Center = new Vector3(-1.8f, 0.6f, 1.5f), Radius = 0.6f, Color = new Vector3(0.8f, 0.8f, 0.9f), IsLight = false, Emission = 0, Material = MaterialType.Metal, Roughness = 0.1f, IOR = 1 },
                // Gold mirror sphere
                new Sphere { Center = new Vector3(1.8f, 0.6f, 1.5f), Radius = 0.6f, Color = new Vector3(1, 0.84f, 0), IsLight = false, Emission = 0, Material = MaterialType.Metal, Roughness = 0.05f, IOR = 1 },
                // Frosted glass sphere
                new Sphere { Center = new Vector3(0, 0.5f, 0.8f), Radius = 0.5f, Color = new Vector3(0.9f, 0.9f, 1), IsLight = false, Emission = 0, Material = MaterialType.Glass, Roughness = 0.15f, IOR = 1.4f },
                
                // Red matte sphere
                new Sphere { Center = new Vector3(-1, 1.5f, 3.5f), Radius = 0.4f, Color = new Vector3(1, 0.3f, 0.3f), IsLight = false, Emission = 0, Material = MaterialType.Diffuse, Roughness = 1, IOR = 1 },
                // Green glass sphere
                new Sphere { Center = new Vector3(1.5f, 1.8f, 4), Radius = 0.5f, Color = new Vector3(0.3f, 1, 0.3f), IsLight = false, Emission = 0, Material = MaterialType.Glass, Roughness = 0.1f, IOR = 1.5f },
                // Blue metal sphere
                new Sphere { Center = new Vector3(-0.5f, 0.7f, 4.5f), Radius = 0.4f, Color = new Vector3(0.3f, 0.5f, 1), IsLight = false, Emission = 0, Material = MaterialType.Metal, Roughness = 0.3f, IOR = 1 }
            };

            planes = new List<Plane>
            {
                // Floor (light gray)
                new Plane { Position = new Vector3(0, 0, 0), Normal = Vector3.UnitY, Color = new Vector3(0.75f, 0.75f, 0.75f) },
                // Back wall (white)
                new Plane { Position = new Vector3(0, 0, 6), Normal = -Vector3.UnitZ, Color = new Vector3(0.9f, 0.9f, 0.9f) },
                // Left wall (deep red)
                new Plane { Position = new Vector3(-4, 0, 0), Normal = Vector3.UnitX, Color = new Vector3(0.9f, 0.2f, 0.2f) },
                // Right wall (deep cyan)
                new Plane { Position = new Vector3(4, 0, 0), Normal = -Vector3.UnitX, Color = new Vector3(0.2f, 0.9f, 0.9f) },
                // Ceiling (white)
                new Plane { Position = new Vector3(0, 4, 0), Normal = -Vector3.UnitY, Color = new Vector3(0.85f, 0.85f, 0.85f) }
            };

            BuildBVH();
        }

        private void BuildBVH()
        {
            bvhNodes = new List<BVHNode>();
            if (spheres.Count > 0)
            {
                BuildBVHRecursive(0, spheres.Count, ref bvhNodes);
            }
        }

        private void BuildBVHRecursive(int start, int end, ref List<BVHNode> nodes)
        {
            if (start >= end) return;

            int nodeIndex = nodes.Count;
            BVHNode node = new BVHNode { BoundsMin = Vector3.One * float.MaxValue, BoundsMax = Vector3.One * float.MinValue };

            // Compute bounds
            for (int i = start; i < end; i++)
            {
                var sphere = spheres[i];
                Vector3 minBound = sphere.Center - Vector3.One * sphere.Radius;
                Vector3 maxBound = sphere.Center + Vector3.One * sphere.Radius;
                node.BoundsMin = Vector3.Min(node.BoundsMin, minBound);
                node.BoundsMax = Vector3.Max(node.BoundsMax, maxBound);
            }

            nodes.Add(node);

            if (end - start <= 2)
            {
                // Leaf node
                node.IsLeaf = true;
                node.SphereIndex = start;
                nodes[nodeIndex] = node;
            }
            else
            {
                // Split on longest axis
                Vector3 extent = node.BoundsMax - node.BoundsMin;
                int axis = 0;
                if (extent.Y > extent.X) axis = 1;
                if (extent.Z > Math.Max(extent.X, extent.Y)) axis = 2;

                float split = node.BoundsMin[axis] + extent[axis] / 2;
                int mid = start;
                for (int i = start; i < end; i++)
                {
                    if (spheres[i].Center[axis] < split) mid++;
                }

                if (mid == start || mid == end) mid = start + (end - start) / 2;

                node.IsLeaf = false;
                node.LeftChild = nodes.Count;
                nodes[nodeIndex] = node;

                BuildBVHRecursive(start, mid, ref nodes);
                node.RightChild = nodes.Count;
                BuildBVHRecursive(mid, end, ref nodes);
            }
        }

        private HitRecord TraceRay(Ray ray, float maxDist = float.MaxValue)
        {
            HitRecord closest = new HitRecord { Hit = false, Distance = maxDist };

            // Check spheres directly (skip BVH for now - simpler and more stable)
            foreach (var sphere in spheres)
            {
                HitRecord hit = RaySphereIntersect(ray, sphere);
                if (hit.Hit && hit.Distance < closest.Distance && hit.Distance > RAY_EPSILON)
                {
                    closest = hit;
                }
            }

            // Check planes
            foreach (var plane in planes)
            {
                HitRecord hit = RayPlaneIntersect(ray, plane);
                if (hit.Hit && hit.Distance < closest.Distance && hit.Distance > RAY_EPSILON)
                {
                    closest = hit;
                }
            }

            return closest;
        }

        private void TraceRayBVH(int nodeIndex, Ray ray, ref HitRecord closest)
        {
            // Disabled - use direct sphere checking instead
        }

        private HitRecord RaySphereIntersect(Ray ray, Sphere sphere)
        {
            HitRecord result = new HitRecord { Hit = false };

            Vector3 oc = ray.Origin - sphere.Center;
            float a = Vector3.Dot(ray.Direction, ray.Direction);
            float b = 2.0f * Vector3.Dot(oc, ray.Direction);
            float c = Vector3.Dot(oc, oc) - sphere.Radius * sphere.Radius;
            float discriminant = b * b - 4 * a * c;

            if (discriminant < 0) return result;

            float t = (-b - (float)Math.Sqrt(discriminant)) / (2 * a);
            if (t <= RAY_EPSILON) t = (-b + (float)Math.Sqrt(discriminant)) / (2 * a);

            if (t > RAY_EPSILON)
            {
                result.Hit = true;
                result.Distance = t;
                result.Point = ray.Origin + ray.Direction * t;
                result.Normal = Vector3.Normalize(result.Point - sphere.Center);
                result.Color = sphere.Color;
                result.IsLight = sphere.IsLight;
                result.Emission = sphere.Emission;
                result.Material = sphere.Material;
                result.Roughness = sphere.Roughness;
                result.IOR = sphere.IOR;
            }

            return result;
        }

        private HitRecord RayPlaneIntersect(Ray ray, Plane plane)
        {
            HitRecord result = new HitRecord { Hit = false };

            float denom = Vector3.Dot(plane.Normal, ray.Direction);
            if (Math.Abs(denom) < 1e-6f) return result;

            float t = Vector3.Dot(plane.Position - ray.Origin, plane.Normal) / denom;
            if (t > RAY_EPSILON)
            {
                result.Hit = true;
                result.Distance = t;
                result.Point = ray.Origin + ray.Direction * t;
                result.Normal = plane.Normal;
                result.Color = plane.Color;
                result.IsLight = false;
                result.Emission = 0;
            }

            return result;
        }

        private bool RayAABBIntersect(Ray ray, Vector3 min, Vector3 max)
        {
            float tmin = 0, tmax = float.MaxValue;

            for (int i = 0; i < 3; i++)
            {
                float invD = 1.0f / ray.Direction[i];
                float t0 = (min[i] - ray.Origin[i]) * invD;
                float t1 = (max[i] - ray.Origin[i]) * invD;

                if (invD < 0) (t0, t1) = (t1, t0);
                tmin = Math.Max(tmin, t0);
                tmax = Math.Min(tmax, t1);

                if (tmax < tmin) return false;
            }

            return true;
        }

        private Vector3 TracePathRecursive(Ray ray, int depth)
        {
            if (depth > 4) return Vector3.Zero;

            HitRecord hit = TraceRay(ray);
            if (!hit.Hit) return Vector3.Zero;

            if (hit.IsLight)
            {
                return hit.Color * hit.Emission;
            }

            Vector3 result = Vector3.Zero;

            // Handle different materials
            if (hit.Material == MaterialType.Glass)
            {
                // Simple transparency for glass
                Vector3 refracted = ray.Direction;  // Straight through for simplicity
                if (hit.Roughness < 0.2f)  // Only for clear glass
                {
                    Ray refractRay = new Ray { Origin = hit.Point + ray.Direction * RAY_EPSILON * 2, Direction = refracted };
                    result += hit.Color * TracePathRecursive(refractRay, depth + 1) * 0.98f;
                }
                else
                {
                    // Frosted glass - scatter a bit
                    Vector3 scattered = ray.Direction + RandomHemisphereDirection(ray.Direction) * hit.Roughness * 0.3f;
                    Ray scatterRay = new Ray { Origin = hit.Point + ray.Direction * RAY_EPSILON * 2, Direction = Vector3.Normalize(scattered) };
                    result += hit.Color * TracePathRecursive(scatterRay, depth + 1) * 0.9f;
                }
            }
            else if (hit.Material == MaterialType.Metal)
            {
                // Metallic reflection
                Vector3 reflected = ray.Direction - 2 * Vector3.Dot(ray.Direction, hit.Normal) * hit.Normal;
                
                // Add roughness
                if (hit.Roughness > 0.01f)
                {
                    Vector3 roughDir = RandomHemisphereDirection(reflected) * hit.Roughness * 0.3f;
                    reflected = Vector3.Normalize(reflected + roughDir);
                }
                
                Ray reflectRay = new Ray { Origin = hit.Point + hit.Normal * RAY_EPSILON, Direction = reflected };
                result += hit.Color * TracePathRecursive(reflectRay, depth + 1) * 0.95f;
            }
            else  // Diffuse material
            {
                // Direct light sampling
                foreach (var sphere in spheres)
                {
                    if (sphere.IsLight)
                    {
                        Vector3 toLight = Vector3.Normalize(sphere.Center - hit.Point);
                        float cosTheta = Math.Max(0, Vector3.Dot(hit.Normal, toLight));
                        
                        if (cosTheta > 0)
                        {
                            Ray shadowRay = new Ray { Origin = hit.Point + hit.Normal * RAY_EPSILON, Direction = toLight };
                            HitRecord shadowHit = TraceRay(shadowRay, Vector3.Distance(hit.Point, sphere.Center));
                            
                            if (!shadowHit.Hit || shadowHit.IsLight)
                            {
                                result += hit.Color * sphere.Emission * cosTheta * 0.85f;
                            }
                        }
                    }
                }

                // Indirect bounce
                if (depth < 3)
                {
                    Vector3 randomDir = RandomHemisphereDirection(hit.Normal);
                    Ray bounceRay = new Ray { Origin = hit.Point + hit.Normal * RAY_EPSILON, Direction = randomDir };
                    result += hit.Color * TracePathRecursive(bounceRay, depth + 1) * 0.25f;
                }
            }

            return result;
        }

        private Vector3 RandomHemisphereDirection(Vector3 normal)
        {
            float u1 = (float)random.NextDouble();
            float u2 = (float)random.NextDouble();

            float theta = (float)Math.Acos(Math.Sqrt(1 - u1));
            float phi = 2 * (float)Math.PI * u2;

            Vector3 right = Math.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            Vector3 tangent = Vector3.Normalize(Vector3.Cross(normal, right));
            Vector3 bitangent = Vector3.Cross(normal, tangent);

            return (float)Math.Sin(theta) * ((float)Math.Cos(phi) * tangent + (float)Math.Sin(phi) * bitangent) + (float)Math.Cos(theta) * normal;
        }

        public class BenchmarkResult
        {
            public double SamplesPerSecond { get; set; }
            public double TotalSamples { get; set; }
            public double ElapsedSeconds { get; set; }
            public double Score { get; set; }
        }

        public BenchmarkResult RunBenchmark(int samplesPerPixel = 1, int imageWidth = 256, int imageHeight = 256, double durationSeconds = 6.0, int numThreads = 1, System.Windows.Forms.Form displayForm = null, System.Windows.Forms.PictureBox pictureBox = null)
        {
            int width = imageWidth;
            int height = imageHeight;
            Vector3 cameraPos = new Vector3(0, 1.5f, -2);
            float fov = 60 * (float)Math.PI / 180;
            float aspect = (float)width / height;

            Vector3[] frameBuffer = new Vector3[width * height];
            
            // Initialize with black
            for (int i = 0; i < frameBuffer.Length; i++)
                frameBuffer[i] = Vector3.Zero;

            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                long totalSamples = 0;
                int tileSize = 64;
                int tilesX = (width + tileSize - 1) / tileSize;
                int tilesY = (height + tileSize - 1) / tileSize;
                int tilesCompleted = 0;
                object tileLock = new object();

                // Create list of all tiles for parallel processing
                var tileList = new List<(int tx, int ty)>();
                for (int ty = 0; ty < tilesY; ty++)
                {
                    for (int tx = 0; tx < tilesX; tx++)
                    {
                        tileList.Add((tx, ty));
                    }
                }

                // Render all tiles in parallel with thread pool distribution
                var parallelOptions = new System.Threading.Tasks.ParallelOptions 
                { 
                    MaxDegreeOfParallelism = numThreads 
                };

                System.Threading.Tasks.Parallel.ForEach(tileList, parallelOptions, tile =>
                {
                    int tx = tile.tx;
                    int ty = tile.ty;
                    int startX = tx * tileSize;
                    int startY = ty * tileSize;
                    int endX = Math.Min(startX + tileSize, width);
                    int endY = Math.Min(startY + tileSize, height);

                    // Each thread renders its own tile
                    var threadRandom = new System.Random();
                    for (int y = startY; y < endY; y++)
                    {
                        for (int x = startX; x < endX; x++)
                        {
                            Vector3 color = Vector3.Zero;
                            
                            // Multiple samples per pixel for quality
                            for (int s = 0; s < samplesPerPixel; s++)
                            {
                                float u = ((float)x + (float)threadRandom.NextDouble()) / width;
                                float v = 1.0f - ((float)y + (float)threadRandom.NextDouble()) / height;

                                float rayX = (2 * u - 1) * aspect * (float)Math.Tan(fov / 2);
                                float rayY = (2 * v - 1) * (float)Math.Tan(fov / 2);

                                Ray ray = new Ray
                                {
                                    Origin = cameraPos,
                                    Direction = Vector3.Normalize(new Vector3(rayX, rayY, 1))
                                };

                                color += TracePathRecursive(ray, 0);
                                lock (this)
                                {
                                    totalSamples++;
                                }
                            }
                            
                            // Average the samples
                            color /= samplesPerPixel;
                            lock (frameBuffer)
                            {
                                frameBuffer[y * width + x] += color;
                            }
                        }
                    }

                    // Update display after each tile completes
                    lock (tileLock)
                    {
                        tilesCompleted++;
                        if (pictureBox != null && displayForm != null)
                        {
                            if (pictureBox.InvokeRequired)
                            {
                                pictureBox.Invoke(new Action(() => {
                                    try
                                    {
                                        Bitmap updateBitmap = CreateBitmapFromBuffer(frameBuffer, width, height, 1);
                                        pictureBox.Image = updateBitmap;
                                        pictureBox.Refresh();
                                    }
                                    catch { }
                                }));
                            }
                            else
                            {
                                try
                                {
                                    Bitmap updateBitmap = CreateBitmapFromBuffer(frameBuffer, width, height, 1);
                                    pictureBox.Image = updateBitmap;
                                    pictureBox.Refresh();
                                }
                                catch { }
                            }
                        }
                    }
                });

                sw.Stop();

                // Final update
                Bitmap finalBitmap = CreateBitmapFromBuffer(frameBuffer, width, height, 1);
                pictureBox.Image = finalBitmap;
                pictureBox.Refresh();
                displayForm.Refresh();
                System.Windows.Forms.Application.DoEvents();

                displayForm.Text = "Path Tracer - Render Complete";

                double elapsed = sw.Elapsed.TotalSeconds;
                double samplesPerSecond = elapsed > 0 ? totalSamples / elapsed : 0;
                double score = elapsed;  // Score is render time in seconds - lower is better

                return new BenchmarkResult
                {
                    SamplesPerSecond = samplesPerSecond,
                    TotalSamples = totalSamples,
                    ElapsedSeconds = elapsed,
                    Score = score
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pathtracer error: {ex.Message}");
                return new BenchmarkResult
                {
                    SamplesPerSecond = 0,
                    TotalSamples = 0,
                    ElapsedSeconds = 0,
                    Score = 0
                };
            }
            finally
            {
                // Close window from UI thread
                if (displayForm != null)
                {
                    try
                    {
                        if (displayForm.InvokeRequired)
                        {
                            displayForm.Invoke(new Action(() => {
                                try { displayForm?.Close(); } catch { }
                                try { displayForm?.Dispose(); } catch { }
                            }));
                        }
                        else
                        {
                            displayForm?.Close();
                            displayForm?.Dispose();
                        }
                    }
                    catch { }
                }
            }
        }

        private Bitmap CreateBitmapFromBuffer(Vector3[] frameBuffer, int width, int height, int frameCount)
        {
            Bitmap bitmap = new Bitmap(width, height);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3 color = frameBuffer[y * width + x];
                    // Divide by frame count to get average
                    if (frameCount > 0)
                        color /= frameCount;
                    
                    int r = (int)(Math.Clamp(color.X, 0, 1) * 255);
                    int g = (int)(Math.Clamp(color.Y, 0, 1) * 255);
                    int b = (int)(Math.Clamp(color.Z, 0, 1) * 255);
                    bitmap.SetPixel(x, y, Color.FromArgb(r, g, b));
                }
            }
            
            return bitmap;
        }
    }
}
