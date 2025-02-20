﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public class SceneVolumeExporter : MonoBehaviour
{
    private string
        rootPath = "/home/huy/semantic-abstraction/groundtruth3dpoints/";

    // https://answers.unity.com/questions/1600764/point-inside-mesh.html
    public bool
    IsInCollider(MeshCollider other, Vector3 point, Vector3 fromDirection)
    {
        Vector3 from = (fromDirection * 5000f);
        Vector3 dir = (point - from).normalized;
        float dist = Vector3.Distance(from, point);

        //fwd
        int hit_count = Cast_Till(from, point, other);

        //back
        dir = (from - point).normalized;
        hit_count += Cast_Till(point, point + (dir * dist), other);

        if (hit_count % 2 == 1)
        {
            return (true);
        }
        return (false);
    }

    int Cast_Till(Vector3 from, Vector3 to, MeshCollider other)
    {
        int counter = 0;
        Vector3 dir = (to - from).normalized;
        float dist = Vector3.Distance(from, to);
        bool Break = false;
        while (!Break)
        {
            Break = true;
            RaycastHit[] hit = Physics.RaycastAll(from, dir, dist);
            for (int tt = 0; tt < hit.Length; tt++)
            {
                if (hit[tt].collider == other)
                {
                    counter++;
                    from = hit[tt].point + dir.normalized * .001f;
                    dist = Vector3.Distance(from, to);
                    Break = false;
                    break;
                }
            }
        }
        return (counter);
    }

    Collider
    checkCollisionWithColliders(
        Vector3 pt,
        Collider[] colliders,
        LayerMask mask
    )
    {
        Collider[] hitColliders = Physics.OverlapSphere(pt, 0.001f, mask);
        foreach (Collider c in hitColliders)
        {
            if (colliders.Contains(c))
            {
                return c;
            }
        }
        foreach (Collider c in colliders)
        {
            if (
                c.GetType() == typeof (MeshCollider)
                    ? (
                    IsInCollider((MeshCollider) c, pt, Vector3.up) &&
                    IsInCollider((MeshCollider) c, pt, Vector3.left) &&
                    IsInCollider((MeshCollider) c, pt, Vector3.forward) &&
                    IsInCollider((MeshCollider) c, pt, -Vector3.up) &&
                    IsInCollider((MeshCollider) c, pt, -Vector3.left) &&
                    IsInCollider((MeshCollider) c, pt, -Vector3.forward)
                    )
                    : false
            )
            {
                return c;
            }
        }
        return null;
    }

    Collider
    checkCollisionProbeWithColliders(
        float radius,
        Vector3 pt,
        Collider[] colliders,
        LayerMask mask
    )
    {
        Collider[] hitColliders = Physics.OverlapSphere(pt, radius, mask);
        foreach (Collider c in hitColliders)
        {
            if (colliders.Contains(c))
            {
                return c;
            }
        }
        return null;
    }

    List<float> RangeFloat(float min, float max, int steps)
    {
        return (List<float>)
        Enumerable
            .Range(0, steps)
            .Select(i => min + (max - min) * ((float) i / (steps - 1)))
            .ToList();
    }

    // Start is called before the first frame update
    void Start()
    {
        UnityEngine.SceneManagement.Scene scene =
            UnityEngine.SceneManagement.SceneManager.GetActiveScene();

        // int seed = Int32.Parse(System.IO.File.ReadAllLines("/home/huy/langmdp/ai2thor/seed.txt")[0]);
        string sceneName = scene.name;
        List<string> receptacles = new List<string>();
        foreach (Contains contain in GameObject.FindObjectsOfType<Contains>())
        {
            BoxCollider receptacleBox = contain.GetComponent<BoxCollider>();
            string transform_matrix =
                receptacleBox
                    .transform
                    .localToWorldMatrix
                    .GetRow(0)
                    .ToString("f8") +
                receptacleBox
                    .transform
                    .localToWorldMatrix
                    .GetRow(1)
                    .ToString("f8") +
                receptacleBox
                    .transform
                    .localToWorldMatrix
                    .GetRow(2)
                    .ToString("f8") +
                receptacleBox
                    .transform
                    .localToWorldMatrix
                    .GetRow(3)
                    .ToString("f8");
            receptacles
                .Add(contain
                    .gameObject
                    .GetComponentInParent<SimObjPhysics>()
                    .name +
                "|" +
                transform_matrix +
                "|" +
                receptacleBox.size.ToString("f5") +
                "|" +
                receptacleBox.center.ToString("f5"));
        }
        File
            .WriteAllLines(rootPath + sceneName + "_receptacles.txt",
            receptacles);
        string directoryPath = rootPath + sceneName;
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory (directoryPath);
        }
        string output_xyz_pts_file = rootPath + sceneName + "/full_xyz_pts.txt";
        string output_objid_pts_file =
            rootPath + sceneName + "/full_objid_pts.txt";
        if (
            File.Exists(output_xyz_pts_file) &&
            File.Exists(output_objid_pts_file)
        )
        {
            return;
        }
        Bounds sceneBounds = new Bounds(Vector3.zero, Vector3.zero);
        List<string> full_xyz_pts = new List<string>();
        List<string> full_objid_pts = new List<string>();
        LayerMask mask = LayerMask.GetMask("SimObjVisible");
        Vector3 samplePos;
        Bounds samplingBounds;
        string objid;
        List<Collider> componentColliders;

        // Get all scene objects
        Dictionary<GameObject, string> sceneObjects =
            new Dictionary<GameObject, string>();
        var sceneRootObjs =
            scene
                .GetRootGameObjects()
                .ToList()
                .ToDictionary(o => o.name, o => o);
        var objectsGameObject = sceneRootObjs["Objects"];
        var structuresGameObject = sceneRootObjs["Structure"];
        foreach (Transform child in sceneRootObjs["Objects"].transform)
        {
            if (
                !child.gameObject.activeSelf ||
                child.name.ToLower().Contains("hideandseek") ||
                child.name.ToLower().Contains("lightray")
            ) continue;
            if (
                !child.gameObject.gameObject.GetComponent<SimObjPhysics>() &&
                !child.gameObject.gameObject.GetComponent<MeshFilter>() &&
                child.gameObject.GetComponentsInChildren<MeshFilter>().Length >
                0
            )
            {
                foreach (var
                    childMeshFilter
                    in
                    child.gameObject.GetComponentsInChildren<MeshFilter>()
                )
                {
                    if (childMeshFilter.name.ToLower().Contains("mesh"))
                    {
                        sceneObjects
                            .Add(childMeshFilter.gameObject, child.name);
                    }
                    else
                    {
                        sceneObjects
                            .Add(childMeshFilter.gameObject,
                            childMeshFilter.name);
                    }
                }
            }
            else
            {
                sceneObjects.Add(child.gameObject, child.name);
            }
        }
        foreach (Transform child in sceneRootObjs["Structure"].transform)
        {
            if (
                !child.gameObject.activeSelf ||
                child.name.ToLower().Contains("hideandseek") ||
                child.name.ToLower().Contains("lightray")
            ) continue;
            if (
                !child.gameObject.gameObject.GetComponent<SimObjPhysics>() &&
                !child.gameObject.gameObject.GetComponent<MeshFilter>() &&
                child.gameObject.GetComponentsInChildren<MeshFilter>().Length >
                0
            )
            {
                foreach (var
                    childMeshFilter
                    in
                    child.gameObject.GetComponentsInChildren<MeshFilter>()
                )
                {
                    if (childMeshFilter.name.ToLower().Contains("mesh"))
                    {
                        sceneObjects
                            .Add(childMeshFilter.gameObject, child.name);
                    }
                    else
                    {
                        sceneObjects
                            .Add(childMeshFilter.gameObject,
                            childMeshFilter.name);
                    }
                }
            }
            else
            {
                sceneObjects.Add(child.gameObject, child.name);
            }
        }
        Dictionary<Collider, GameObject> colliderMap =
            new Dictionary<Collider, GameObject>();
        foreach (GameObject o in sceneObjects.Keys)
        {
            Collider[] objColliders =
                o
                    .GetComponentsInChildren<Collider>()
                    .Where(col => !col.isTrigger)
                    .ToArray();
            if (objColliders.Length == 0)
            {
                MeshFilter[] objMeshFilters =
                    o.gameObject.GetComponentsInChildren<MeshFilter>();
                if (objMeshFilters.Length > 0)
                {
                    Debug
                        .Log(o.name +
                        " doesn't have colliders. Adding one based on mesh");
                    MeshCollider mc =
                        objMeshFilters[0]
                            .gameObject
                            .AddComponent<MeshCollider>();
                    mc.sharedMesh = objMeshFilters[0].mesh;
                    mc.convex = false;
                    objColliders =
                        o.gameObject.GetComponentsInChildren<Collider>();
                }
                else
                {
                    continue;
                }
            }

            // replace all simple colliders with mesh colliders if possible
            for (int idx = 0; idx < objColliders.Length; idx++)
            {
                MeshFilter objMeshFilter =
                    objColliders[idx].gameObject.GetComponent<MeshFilter>();
                if (
                    objColliders[idx].GetType() != typeof (MeshCollider) &&
                    objMeshFilter
                )
                {
                    Debug
                        .Log(objColliders[idx].name +
                        " doesn't have mesh collider. Replacing.");
                    Destroy(objColliders[idx]);
                    MeshCollider mc =
                        objMeshFilter.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = objMeshFilter.mesh;
                    mc.convex = false;
                    objColliders[idx] = mc;
                }
            }

            foreach (Collider childCollider in objColliders)
            {
                if (childCollider.isTrigger) continue;
                Debug.Log(o.name + " has collider of " + childCollider.name);
                if (!colliderMap.ContainsKey(childCollider))
                {
                    colliderMap.Add (childCollider, o);
                }
                else
                {
                    colliderMap[childCollider] = o;
                }
                sceneBounds.Encapsulate(childCollider.bounds);
            }
        }

        float voxSize = 0.02f;

        // coarse grain scene grid first
        var xs =
            RangeFloat(sceneBounds.min[0],
            sceneBounds.max[0],
            (int)((sceneBounds.max[0] - sceneBounds.min[0]) / voxSize));
        var ys =
            RangeFloat(sceneBounds.min[1],
            sceneBounds.max[1],
            (int)((sceneBounds.max[1] - sceneBounds.min[1]) / voxSize));
        var zs =
            RangeFloat(sceneBounds.min[2],
            sceneBounds.max[2],
            (int)((sceneBounds.max[2] - sceneBounds.min[2]) / voxSize));

        Collider
            componentCollider,
            c;
        SimObjPhysics[] simObjComponents;
        Collider[] allColliders = colliderMap.Keys.ToArray();
        foreach (var x in xs)
        {
            foreach (var y in ys)
            {
                foreach (var z in zs)
                {
                    objid = "empty";
                    c =
                        checkCollisionProbeWithColliders(// 0.6203504f * voxSize, //rough conversion from square to sphere with same volume
                        voxSize,
                        new Vector3(x, y, z),
                        allColliders,
                        mask);
                    if (c && colliderMap.ContainsKey(c))
                    {
                        simObjComponents =
                            colliderMap[c]
                                .GetComponentsInChildren<SimObjPhysics>();
                        if (simObjComponents.Length > 0)
                        {
                            objid = simObjComponents[0].objectID;
                        }
                        else
                        {
                            objid =
                                sceneObjects[colliderMap[c]] +
                                "|" +
                                colliderMap[c].transform.position.x +
                                "|" +
                                +colliderMap[c].transform.position.y +
                                "|" +
                                colliderMap[c].transform.position.z;
                        }
                    }

                    full_xyz_pts.Add(x + "|" + y + "|" + z);
                    full_objid_pts.Add (objid);
                }
            }
        }
        File.WriteAllLines (output_objid_pts_file, full_objid_pts);
        File.WriteAllLines (output_xyz_pts_file, full_xyz_pts);
    }
}
