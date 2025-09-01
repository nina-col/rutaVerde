
using UnityEngine;
public class Car_1 : MonoBehaviour
{

    public GameObject PrefabCoche;
    public int numCar = 5;
    public float rangoRandom = 20f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        for (int i = 0; i < numCar; i++)
        {
            Vector3 randomPos = new Vector3(
                Random.Range(-rangoRandom, rangoRandom),


                0,

                Random.Range(-rangoRandom, rangoRandom)
            );

            GameObject car = Instantiate(PrefabCoche, randomPos, Quaternion.identity);

            if (i % 2 == 0)
                car.transform.rotation = Quaternion.Euler(0, 0, 0);

            else
                car.transform.rotation = Quaternion.Euler(0, 0, 0);


        }
    }
}



