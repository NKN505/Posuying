using UnityEngine;

public class PlayerVisual : MonoBehaviour
{
    [SerializeField] private GameObject fpArms;   // manos FP
    [SerializeField] private GameObject bodyTP;   // cuerpo TP

    void Start()
    {
        bool isLocalPlayer = true; 

        fpArms.SetActive(isLocalPlayer);   // manos visibles solo para ti
        bodyTP.SetActive(!isLocalPlayer);  // cuerpo visible solo para otros
    }
    
    }
