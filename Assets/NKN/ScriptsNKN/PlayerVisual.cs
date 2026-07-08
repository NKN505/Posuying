using UnityEngine;

public class PlayerVisual : MonoBehaviour
{

    [SerializeField] private GameObject fpArms;
    [SerializeField] private GameObject genSWAT;
    [SerializeField] private LODGroup lodGroup;

    /*

    "Voy a trabajar con un objeto llamado fpArms."
    "Voy a trabajar con otro objeto llamado genSWAT."
    */


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        bool isLocalPlayer = true;

        if (isLocalPlayer)
        {
            fpArms.SetActive(true);
            genSWAT.SetActive(false);

            if (lodGroup != null)
                lodGroup.enabled = false; // Desactiva todos los LOD
        }
        else
        {
            fpArms.SetActive(false);
            genSWAT.SetActive(true);

            if (lodGroup != null)
                lodGroup.enabled = true;
        }
    }
        
    }
