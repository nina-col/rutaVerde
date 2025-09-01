using UnityEngine;

public class create_Instance : MonoBehaviour
{
    public GameObject Prefab;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        for (int i = 0; i < 5; i++)
        {
            float x=Random.Range(-10f, 10f);
            float z=Random.Range(-10f, 10f);
            Instantiate(Prefab, new Vector3(x, 0, z), Quaternion.Euler(0, i * 72, 0));
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
