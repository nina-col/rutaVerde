using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

public class BackendController : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject truckPrefab;
    public GameObject containerPrefab;

    [Header("UI")]
    public TextMeshProUGUI stepCounterText;   // 👈 arrastra el TMP del Canvas
    public TextMeshProUGUI endMessageText;    // 👈 arrastra el TMP del Canvas
    public TextMeshProUGUI progressText;      // 👈 texto adicional para mostrar porcentaje

    private Dictionary<int, GameObject> trucks = new Dictionary<int, GameObject>();
    private List<GameObject> containers = new List<GameObject>();

    private int currentStep = 0;
    private int totalSteps = 0;   // 👈 ahora es privado, Unity no lo resetea a 0
    private bool simulationEnded = false;
    private Coroutine nextStepRoutine = null;

    // -------------------------------
    // Modelos que coinciden con backend
    [System.Serializable]
    public class Truck {
        public int id;
        public int[] pos;
        public int load;
    }

    [System.Serializable]
    public class Container {
        public int[] pos;
        public int fill;
    }

    [System.Serializable]
    public class Session {
        public int gridX;
        public int gridY;
        public int totalSteps;   // 👈 debe coincidir con backend (camelCase)
        public List<Truck> trucks;
        public List<Container> containers;
    }

    [System.Serializable]
    public class StepDTO {
        public int t;
        public int x;
        public int y;
        public int carrying;
        public string action;
        public bool done;
    }
    // -------------------------------

    void Start()
    {
        if (endMessageText != null)
            endMessageText.gameObject.SetActive(false);
        // Inicializa reiniciando backend y limpiando escena/contadores
        StartCoroutine(InitializeSimulation());
    }

    void Update()
    {
        // Tecla R para reiniciar la simulación y cargar parámetros actualizados
        if (Input.GetKeyDown(KeyCode.R))
        {
            Debug.Log("🔄 Recargando simulación con parámetros actualizados...");
            StartCoroutine(InitializeSimulation());
        }
    }

    IEnumerator InitializeSimulation()
    {
        // 1) Limpiar escena y UI
        ClearScene();
        currentStep = 0;
        simulationEnded = false;
        UpdateStepUI();
        if (endMessageText != null) endMessageText.gameObject.SetActive(false);
        
        Debug.Log("🔄 Iniciando nueva simulación - Limpiando escena y reseteando contadores");

        // 2) Reset completo del backend (reinicia t y el productor)
        yield return StartCoroutine(ResetSimulation());

        // 3) Obtener sesión e iniciar consumo de pasos
        yield return StartCoroutine(GetSession());
    }

    IEnumerator ResetSimulation()
    {
        using (UnityWebRequest req = new UnityWebRequest("http://127.0.0.1:8000/simulation/reset", "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes("{}");
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("⚠️ Reset backend falló: " + req.error + ". Intentando continuar…");
            }
            else
            {
                Debug.Log("🔄 Backend reseteado: " + req.downloadHandler.text);
            }
        }
    }

    void ClearScene()
    {
        // Detener loop previo si existe
        if (nextStepRoutine != null)
        {
            StopCoroutine(nextStepRoutine);
            nextStepRoutine = null;
        }
        // Destruir camiones
        foreach (var kv in trucks)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        trucks.Clear();
        // Destruir contenedores
        foreach (var c in containers)
        {
            if (c != null) Destroy(c);
        }
        containers.Clear();
    }

    IEnumerator GetSession()
    {
        // Opcional: también podrías usar /session?reset=true y omitir ResetSimulation()
        using (UnityWebRequest req = UnityWebRequest.Get("http://127.0.0.1:8000/session"))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Error al obtener sesión: " + req.error);
            }
            else
            {
                Session session = JsonUtility.FromJson<Session>(req.downloadHandler.text);
                totalSteps = session.totalSteps;   // 👈 ahora sí se asigna bien
                Debug.Log($"✅ Sesión inicializada - Total steps: {totalSteps} | Grid: {session.gridX}x{session.gridY} | Camiones: {session.trucks.Count} | Contenedores: {session.containers.Count}");
                Debug.Log($"🔄 PARÁMETROS ACTUALIZADOS: totalSteps ahora es {totalSteps}");

                // Crear contenedores
                foreach (var c in session.containers)
                {
                    Vector3 pos = new Vector3(c.pos[0], 0, c.pos[1]);
                    GameObject containerObj = Instantiate(containerPrefab, pos, Quaternion.identity);
                    containers.Add(containerObj);
                }

                // Crear camiones
                foreach (var t in session.trucks)
                {
                    Vector3 pos = new Vector3(t.pos[0], 0, t.pos[1]);
                    GameObject truckObj = Instantiate(truckPrefab, pos, Quaternion.identity);
                    trucks[t.id] = truckObj;
                }

                UpdateStepUI();
                // Asegura un único loop de pasos
                if (nextStepRoutine != null)
                {
                    StopCoroutine(nextStepRoutine);
                }
                nextStepRoutine = StartCoroutine(GetNextStep());
            }
        }
    }

    IEnumerator GetNextStep()
    {
        while (!simulationEnded)
        {
            // Obtener pasos para todos los camiones (0, 1, 2)
            for (int truckId = 0; truckId < 3; truckId++)
            {
                yield return StartCoroutine(GetStepForTruck(truckId));
            }
            yield return new WaitForSeconds(0.2f); // 👈 ajusta la velocidad de actualización
        }
    }

    IEnumerator GetStepForTruck(int truckId)
    {
        using (UnityWebRequest req = UnityWebRequest.Get($"http://127.0.0.1:8000/step/next?robot_id={truckId}"))
        {
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success && req.responseCode == 200)
            {
                StepDTO step = JsonUtility.FromJson<StepDTO>(req.downloadHandler.text);

                if (trucks.ContainsKey(truckId))
                {
                    trucks[truckId].transform.position = new Vector3(step.x, 0, step.y);
                }

                // Solo actualizar UI con el camión 0 para evitar spam
                if (truckId == 0)
                {
                    currentStep = step.t;
                    UpdateStepUI();
                    
                    // Debug detallado cada 100 pasos o en eventos importantes
                    if (step.t % 100 == 0 || step.done)
                    {
                        Debug.Log($"📊 Paso {step.t}: Camión {truckId} en ({step.x},{step.y}) con carga {step.carrying} - Acción: {step.action}");
                    }

                    if (step.done)
                    {
                        simulationEnded = true;
                        ShowEndMessage();
                        yield break;
                    }
                }
                else
                {
                    // Log ocasional para otros camiones
                    if (step.t % 200 == 0)
                    {
                        Debug.Log($"🚛 Camión {truckId} en ({step.x},{step.y}) con carga {step.carrying}");
                    }
                }
            }
            else if (req.responseCode == 204)
            {
                // No hay pasos disponibles para este camión, esto es normal
                // Debug.Log($"⏳ Esperando próximo paso para camión {truckId}...");
            }
            else
            {
                Debug.LogWarning($"⚠️ Error obteniendo paso para camión {truckId}: {req.error} (Código: {req.responseCode})");
            }
        }
    }

    void UpdateStepUI()
    {
        if (stepCounterText != null)
        {
            stepCounterText.text = $"Pasos: {currentStep} / {totalSteps}";
        }
        
        if (progressText != null)
        {
            float progress = totalSteps > 0 ? (float)currentStep / totalSteps * 100f : 0f;
            progressText.text = $"Progreso: {progress:F1}%";
        }
        else
        {
            // Si progressText no está asignado, mostrar progreso en stepCounterText
            if (stepCounterText != null)
            {
                float progress = totalSteps > 0 ? (float)currentStep / totalSteps * 100f : 0f;
                stepCounterText.text = $"Pasos: {currentStep} / {totalSteps} ({progress:F1}%)";
                Debug.Log($"🔄 UI actualizada: {currentStep}/{totalSteps} pasos ({progress:F1}%)");
            }
        }
        
        // Debug adicional en consola cada 50 pasos
        if (currentStep % 50 == 0 || currentStep == totalSteps)
        {
            Debug.Log($"🎯 Simulación - Paso {currentStep}/{totalSteps} ({(totalSteps > 0 ? (float)currentStep / totalSteps * 100f : 0f):F1}%)");
        }
    }

    void ShowEndMessage()
    {
        if (endMessageText != null)
        {
            endMessageText.gameObject.SetActive(true);
            endMessageText.text = $"✅ Simulación completada\n{currentStep}/{totalSteps} pasos ejecutados";
        }
        Debug.Log($"✅ Simulación finalizada - {currentStep}/{totalSteps} pasos completados");
    }
}
