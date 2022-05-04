﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;
using System;

public class SceneVolumeExporter : MonoBehaviour {
    private int numPtsPerObj = 500;

    // https://answers.unity.com/questions/1600764/point-inside-mesh.html
    public bool IsInCollider(MeshCollider other, Vector3 point) {
        Vector3 from = (Vector3.up * 5000f);
        Vector3 dir = (point - from).normalized;
        float dist = Vector3.Distance(from, point);
        //fwd      
        int hit_count = Cast_Till(from, point, other);
        //back
        dir = (from - point).normalized;
        hit_count += Cast_Till(point, point + (dir * dist), other);

        if (hit_count % 2 == 1) {
            return (true);
        }
        return (false);
    }

    int Cast_Till(Vector3 from, Vector3 to, MeshCollider other) {
        int counter = 0;
        Vector3 dir = (to - from).normalized;
        float dist = Vector3.Distance(from, to);
        bool Break = false;
        while (!Break) {
            Break = true;
            RaycastHit[] hit = Physics.RaycastAll(from, dir, dist);
            for (int tt = 0; tt < hit.Length; tt++) {
                if (hit[tt].collider == other) {
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

    Collider checkCollisionWithColliders(Vector3 pt, Collider[] colliders, LayerMask mask) {
        Collider[] hitColliders = Physics.OverlapSphere(pt, 0.01f, mask);
        foreach (Collider c in hitColliders) {
            if (colliders.Contains(c)) {
                return c;
            }
        }
        foreach (Collider c in colliders) {
            if (c.GetType() == typeof(MeshCollider) ? IsInCollider((MeshCollider)c, pt) : false) {
                return c;
            }
        }
        return null;
    }

    // Start is called before the first frame update
    void Start() {
        UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        string sceneName = scene.name;
        string directoryPath = "/home/huy/langmdp/ai2thor/" + sceneName;
        if (!Directory.Exists(directoryPath)) {
            Directory.CreateDirectory(directoryPath);
        }
        string output_xyz_pts_file = directoryPath + "/full_xyz_pts.txt";
        string output_objid_pts_file = directoryPath + "/full_objid_pts.txt";
        if (File.Exists(output_xyz_pts_file) && File.Exists(output_objid_pts_file)) {
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
        var random = new System.Random();
        Collider componentCollider, c;

        // Get all scene objects
        Dictionary<GameObject, string> sceneObjects = new Dictionary<GameObject, string>();
        var sceneRootObjs = scene.GetRootGameObjects().ToList().ToDictionary(o => o.name, o => o);
        var objectsGameObject = sceneRootObjs["Objects"];
        var structuresGameObject = sceneRootObjs["Structure"];
        foreach (Transform child in sceneRootObjs["Objects"].transform) {
            sceneObjects.Add(child.gameObject, child.name);
        }
        foreach (Transform child in sceneRootObjs["Structure"].transform) {
            sceneObjects.Add(child.gameObject, child.name);
        }
        Dictionary<Collider, GameObject> colliderMap = new Dictionary<Collider, GameObject>();
        foreach (GameObject o in sceneObjects.Keys) {
            Collider[] objColliders = o.GetComponentsInChildren<Collider>();
            MeshFilter[] objMeshFilters = o.gameObject.GetComponentsInChildren<MeshFilter>();
            if (objColliders.Length == 0) {
                Debug.Log(o.name + " doesn't have colliders. Adding one based on mesh");
                MeshFilter meshFilter = objMeshFilters[0];
                MeshCollider mc = o.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = meshFilter.mesh;
                mc.convex = false;
                objColliders = o.gameObject.GetComponentsInChildren<Collider>();
            }

            // replace all simple colliders with mesh colliders if possible
            for (int idx = 0; idx < objColliders.Length; idx++) {
                MeshFilter objMeshFilter = objColliders[idx].gameObject.GetComponent<MeshFilter>();
                if (objColliders[idx].GetType() != typeof(MeshCollider) && objMeshFilter) {
                    Debug.Log(objColliders[idx].name + " doesn't have mesh collider. Replacing.");
                    Destroy(objColliders[idx]);
                    MeshCollider mc = o.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = objMeshFilter.mesh;
                    mc.convex = false;
                    objColliders[idx] = mc;
                }
            }


            foreach (Collider childCollider in objColliders) {
                if (!colliderMap.ContainsKey(childCollider)) {
                    colliderMap.Add(childCollider, o);
                    sceneBounds.Encapsulate(childCollider.bounds);
                }
            }
        }

        Collider[] allColliders = colliderMap.Keys.ToArray();
        foreach (var kvp in sceneObjects) {
            componentColliders = kvp.Key.GetComponentsInChildren<Collider>().ToList();
            if (componentColliders.Count == 0) {
                Debug.LogWarning(kvp.Value + " has no collider");
                continue;
            }
            int tries = 0;
            int num_positives = 0;
            int target = numPtsPerObj;
            if (kvp.Key.tag == "Structure" || kvp.Value.Contains("Floor")
            ) {
                target *= 50;
            } else if (kvp.Value.Contains("Table")
              || kvp.Value.Contains("Drawer")
              || kvp.Value.Contains("Cabinet")
              || kvp.Value.Contains("Bathtub")
              || kvp.Value.Contains("Toilet")
              || kvp.Value.Contains("Desk")
              || kvp.Value.Contains("Sink")
              || kvp.Value.Contains("Chair")
              || kvp.Value.Contains("Fridge")) {
                target *= 20;
            }

            while (num_positives < target) {
                tries += 1;
                if (tries > target * 5) {
                    Debug.Log("FAILED for " + kvp.Key.name + "| got " + num_positives);
                    break;
                }
                componentCollider = componentColliders[random.Next(componentColliders.Count)];
                samplingBounds = componentCollider.bounds;
                sceneBounds.Encapsulate(samplingBounds);
                samplePos.x = UnityEngine.Random.Range(samplingBounds.min[0], samplingBounds.max[0]);
                samplePos.y = UnityEngine.Random.Range(samplingBounds.min[1], samplingBounds.max[1]);
                samplePos.z = UnityEngine.Random.Range(samplingBounds.min[2], samplingBounds.max[2]);

                objid = "empty";
                c = checkCollisionWithColliders(samplePos, allColliders, mask);
                if (c && colliderMap.ContainsKey(c)) {
                    SimObjPhysics[] simObjComponents = colliderMap[c].GetComponentsInChildren<SimObjPhysics>();
                    if (simObjComponents.Length > 0) {
                        objid = simObjComponents[0].objectID;
                    } else {
                        objid = colliderMap[c].name + "|" +
                    colliderMap[c].transform.position.x + "|" +
                    +colliderMap[c].transform.position.y + "|" +
                    colliderMap[c].transform.position.z;
                    }
                }
                full_xyz_pts.Add(samplePos.x + "|" + samplePos.y + "|" + samplePos.z);
                full_objid_pts.Add(objid);

                if (c && componentColliders.Contains(c)) {
                    num_positives += 1;
                }

            }
        }
        for (int i = 0; i < numPtsPerObj * 500; i++) {
            samplePos.x = UnityEngine.Random.Range(sceneBounds.min[0], sceneBounds.max[0]);
            samplePos.y = UnityEngine.Random.Range(sceneBounds.min[1], sceneBounds.max[1]);
            samplePos.z = UnityEngine.Random.Range(sceneBounds.min[2], sceneBounds.max[2]);
            // check
            objid = "empty";
            c = checkCollisionWithColliders(samplePos, allColliders, mask);
            if (c && colliderMap.ContainsKey(c)) {
                SimObjPhysics[] simObjComponents = colliderMap[c].GetComponentsInChildren<SimObjPhysics>();
                if (simObjComponents.Length > 0) {
                    objid = simObjComponents[0].objectID;
                } else {
                    objid = colliderMap[c].name + "|" +
                colliderMap[c].transform.position.x + "|" +
                +colliderMap[c].transform.position.y + "|" +
                colliderMap[c].transform.position.z;
                }
            }

            full_xyz_pts.Add(samplePos.x + "|" + samplePos.y + "|" + samplePos.z);
            full_objid_pts.Add(objid);
        }
        File.WriteAllLines(output_objid_pts_file, full_objid_pts);
        File.WriteAllLines(output_xyz_pts_file, full_xyz_pts);
    }

}
