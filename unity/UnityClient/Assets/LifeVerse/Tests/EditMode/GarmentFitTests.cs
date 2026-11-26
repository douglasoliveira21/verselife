using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using LifeVerse.Shared.Avatar;
using LifeVerse.Unity.Avatar;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LifeVerse.Unity.Tests.EditMode
{
    /// <summary>A skinned garment made on one body worn by another with different proportions.</summary>
    public sealed class GarmentFitTests
    {
        private static IEnumerator Done(Task task)
        {
            var until = System.DateTime.UtcNow.AddSeconds(30); while (!task.IsCompleted) { Assert.Less(System.DateTime.UtcNow, until, "the model did not load in time"); yield return null; }
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }

        /// <summary>Where the skinning puts a vertex now (every box of the template follows one bone).</summary>
        private static Vector3 Skinned(SkinnedMeshRenderer skin, int vertex, Matrix4x4[] poses)
        {
            var weight = skin.sharedMesh.boneWeights[vertex]; var bone = weight.boneIndex0;
            return (skin.bones[bone].localToWorldMatrix * poses[bone]).MultiplyPoint3x4(skin.sharedMesh.vertices[vertex]);
        }

        [UnityTest]
        public IEnumerator AGarmentFitsTheRestPoseOfWhoeverWearsIt()
        {
            var parent = new GameObject("fit"); var garment = new GameObject("garment");
            try
            {
                var bodyImport = new GLTFast.GltfImport(deferAgent: new GLTFast.UninterruptedDeferAgent()); var bodyLoad = bodyImport.LoadGltfBinary(HumanoidTemplate.Build()); yield return Done(bodyLoad);
                var creating = SkeletalBody.CreateAsync(parent.transform, bodyImport); yield return Done(creating); var body = creating.Result; Assert.IsNotNull(body);
                // the garment was made on a body with arms a third longer
                var glb = WearableTemplate.Clothing("shirt", WearableTemplate.ShirtBones, options: new TemplateOptions { ArmStretch = 1.35f });
                var import = new GLTFast.GltfImport(deferAgent: new GLTFast.UninterruptedDeferAgent()); var load = import.LoadGltfBinary(glb); yield return Done(load);
                var placing = import.InstantiateMainSceneAsync(garment.transform); yield return Done(placing);
                var skin = garment.GetComponentsInChildren<SkinnedMeshRenderer>().Single(); var count = skin.sharedMesh.vertexCount;
                var made = Enumerable.Range(0, count).Select(i => Skinned(skin, i, skin.sharedMesh.bindposes)).Select(garment.transform.InverseTransformPoint).ToArray();   // the garment as its file shows it, in its model
                var ownPoses = skin.sharedMesh.bindposes.ToArray();

                Assert.IsTrue(AvatarWardrobe.Bind(new[] { skin }, body, garment.transform));
                Assert.IsTrue(skin.bones.All(b => b.IsChildOf(body.Content)), "the garment follows the bones of the wearer");
                var worst = 0f; var naive = 0f;
                for (var i = 0; i < count; i++)
                {
                    var wanted = body.Content.TransformPoint(made[i]);
                    worst = Mathf.Max(worst, Vector3.Distance(Skinned(skin, i, skin.sharedMesh.bindposes), wanted)); naive = Mathf.Max(naive, Vector3.Distance(Skinned(skin, i, ownPoses), wanted));
                }
                Assert.Less(worst, .002f, "at rest the garment shows where it was made");
                Assert.Greater(naive, .05f, "keeping the garment's own rest pose would have moved it: the two skeletons differ");
                // and it bends with the body from there
                var arm = body.BoneOf("upper_arm_l"); var before = Enumerable.Range(0, count).Select(i => Skinned(skin, i, skin.sharedMesh.bindposes)).ToArray(); arm.Rotate(0f, 0f, 60f, Space.World);
                Assert.IsTrue(Enumerable.Range(0, count).Any(i => Vector3.Distance(before[i], Skinned(skin, i, skin.sharedMesh.bindposes)) > .1f), "the sleeve follows the arm");
            }
            finally { Object.DestroyImmediate(parent); Object.DestroyImmediate(garment); }
        }
    }
}
