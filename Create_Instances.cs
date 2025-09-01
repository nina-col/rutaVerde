using System.Linq.Expressions;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class Create_Instances : MonoBehaviour
{

    public GameObject PrefabCoche;

    public int numInstances = 5;

    private GameObject[] instancias;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        instancias = new GameObject[numInstances];

        for (int i = 0; i < numInstances; i++)
        {
            float x = UnityEngine.Random.Range(-50f, 50f);
            float y = 0f;
            float z = UnityEngine.Random.Range(-50f, 50f);

            GameObject instancia = Instantiate(PrefabCoche, new Vector3(x, y, z), Quaternion.Euler(0, 0, 0));

            instancias[i] = instancia;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
