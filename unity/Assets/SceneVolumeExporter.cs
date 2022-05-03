using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;
using System;

public class SceneVolumeExporter : MonoBehaviour {
    private int numPtsPerObj = 2000;

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
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
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
        List<SimObjPhysics> objects = GameObject.FindObjectsOfType<SimObjPhysics>().ToList();
        Dictionary<Collider, SimObjPhysics> colliderObjMap = new Dictionary<Collider, SimObjPhysics>();
        foreach (SimObjPhysics component in objects) {
            foreach (Collider childCollider in component.gameObject.GetComponentsInChildren<Collider>()) {
                if (!colliderObjMap.ContainsKey(childCollider)) {
                    colliderObjMap.Add(childCollider, component);
                    sceneBounds.Encapsulate(childCollider.bounds);
                }
            }
        }
        List<StructureObject> structures = GameObject.FindObjectsOfType<StructureObject>().ToList();
        Dictionary<StructureObject, StructureObject> topLevelStructures = new Dictionary<StructureObject, StructureObject>();
        foreach (StructureObject structure in structures) {
            if (!topLevelStructures.ContainsKey(structure)) {
                topLevelStructures.Add(structure, structure);
            }
            foreach (StructureObject childStructure in structure.gameObject.GetComponentsInChildren<StructureObject>()) {
                if (!topLevelStructures.ContainsKey(childStructure)) {
                    topLevelStructures.Add(childStructure, structure);
                }
                topLevelStructures[childStructure] = structure;
            }
        }
        structures = topLevelStructures.Values.ToList();
        Dictionary<Collider, StructureObject> colliderStructureMap = new Dictionary<Collider, StructureObject>();
        foreach (StructureObject structure in structures) {
            Collider[] objColliders = structure.gameObject.GetComponentsInChildren<Collider>();
            if (objColliders.Length == 0) {
                Debug.Log(structure.name + "|" + objColliders.Length);
                MeshFilter meshFilter = structure.gameObject.GetComponentsInChildren<MeshFilter>()[0];
                MeshCollider mc = structure.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = meshFilter.mesh;
                mc.convex = false;
                objColliders = structure.gameObject.GetComponentsInChildren<Collider>();
                Debug.Log(structure.name + "|" + objColliders.Length);
            }
            foreach (Collider childCollider in objColliders) {
                if (!colliderStructureMap.ContainsKey(childCollider)) {
                    colliderStructureMap.Add(childCollider, structure);
                    sceneBounds.Encapsulate(childCollider.bounds);
                }
            }
        }
        List<string> full_xyz_pts = new List<string>();
        List<string> full_objid_pts = new List<string>();

        LayerMask mask = LayerMask.GetMask("SimObjVisible");
        Vector3 samplePos;
        Bounds samplingBounds;
        string objid;
        var random = new System.Random();
        Collider componentCollider;
        Dictionary<GameObject, string> sceneObjects = new Dictionary<GameObject, string>();
        structures.ForEach(
            structure => {
                if (!sceneObjects.ContainsKey(structure.gameObject)) {
                    sceneObjects.Add(structure.gameObject, structure.name + "|" +
                    structure.transform.position.x + "|" +
                    +structure.transform.position.y + "|" +
                    structure.transform.position.z);
                }
            });
        objects.ForEach(obj => {
            if (!sceneObjects.ContainsKey(obj.gameObject))
                sceneObjects.Add(obj.gameObject, obj.objectID);
        });
        Collider[] allColliders = colliderObjMap.Keys.Concat(colliderStructureMap.Keys).ToArray();
        Collider c;
        foreach (var kvp in sceneObjects) {
            List<Collider> componentColliders = kvp.Key.GetComponentsInChildren<Collider>().ToList();
            if (componentColliders.Count == 0) {
                Debug.Log(kvp.Value + " has no collider");
                continue;
            }
            List<string> componentPositives = new List<string>();
            List<string> objidPositives = new List<string>();
            int tries = 0;
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

            while (componentPositives.Count < target) {
                tries += 1;
                if (tries > target * 100) {
                    Debug.Log("FAILED for " + kvp.Key.name + "| got " + componentPositives.Count);
                    break;
                }
                componentCollider = componentColliders[random.Next(componentColliders.Count)];
                samplingBounds = componentCollider.bounds;
                samplePos.x = UnityEngine.Random.Range(samplingBounds.min[0], samplingBounds.max[0]);
                samplePos.y = UnityEngine.Random.Range(samplingBounds.min[1], samplingBounds.max[1]);
                samplePos.z = UnityEngine.Random.Range(samplingBounds.min[2], samplingBounds.max[2]);

                // check
                if (checkCollisionWithColliders(samplePos, componentColliders.ToArray(), mask)) {
                    componentPositives.Add(samplePos.x + "|" + samplePos.y + "|" + samplePos.z);
                    objidPositives.Add(kvp.Value);
                } else {
                    objid = "empty";
                    c = checkCollisionWithColliders(samplePos, allColliders, mask);
                    if (c) {
                        if (colliderObjMap.ContainsKey(c)) {
                            objid = colliderObjMap[c].objectID;
                        } else if (colliderStructureMap.ContainsKey(c)) {
                            objid = colliderStructureMap[c].name + "|" +
                            colliderStructureMap[c].transform.position.x + "|" +
                            +colliderStructureMap[c].transform.position.y + "|" +
                            colliderStructureMap[c].transform.position.z;
                        }
                    }
                    full_xyz_pts.Add(samplePos.x + "|" + samplePos.y + "|" + samplePos.z);
                    full_objid_pts.Add(objid);
                }
            }
            full_xyz_pts = full_xyz_pts.Concat(componentPositives).ToList();
            full_objid_pts = full_objid_pts.Concat(objidPositives).ToList();
        }
        for (int i = 0; i < numPtsPerObj * 500; i++) {
            samplePos.x = UnityEngine.Random.Range(sceneBounds.min[0], sceneBounds.max[0]);
            samplePos.y = UnityEngine.Random.Range(sceneBounds.min[1], sceneBounds.max[1]);
            samplePos.z = UnityEngine.Random.Range(sceneBounds.min[2], sceneBounds.max[2]);
            // check
            objid = "empty";
            c = checkCollisionWithColliders(samplePos, allColliders, mask);
            if (c) {
                if (colliderObjMap.ContainsKey(c)) {
                    objid = colliderObjMap[c].objectID;
                } else if (colliderStructureMap.ContainsKey(c)) {
                    objid = colliderStructureMap[c].name + "|" +
                    colliderStructureMap[c].transform.position.x + "|" +
                    +colliderStructureMap[c].transform.position.y + "|" +
                    colliderStructureMap[c].transform.position.z;
                }
            }

            full_xyz_pts.Add(samplePos.x + "|" + samplePos.y + "|" + samplePos.z);
            full_objid_pts.Add(objid);
        }
        File.WriteAllLines(output_objid_pts_file, full_objid_pts);
        File.WriteAllLines(output_xyz_pts_file, full_xyz_pts);
    }

}
