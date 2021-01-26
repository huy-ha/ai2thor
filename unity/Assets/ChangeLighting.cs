﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ChangeLighting : MonoBehaviour {
    //9 elements right now
    public GameObject [] Lights;

    public void SetLights(int lightset) {
        foreach (GameObject go in Lights) {
            go.SetActive(false);
        }

        Lights[lightset - 1].SetActive(true);
    }
}
