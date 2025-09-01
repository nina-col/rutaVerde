from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import List, Dict, Any, Optional
import json
import asyncio
import threading
import time
from agents2 import GarbageEnvironment, TrashContainerAgent, TrashTruckAgent

app = FastAPI(title="Garbage Collection Simulation API", version="1.0.0")

# Configurar CORS para permitir conexiones desde Unity
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # En producción, especificar dominios específicos
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Modelos Pydantic para las respuestas
class Position(BaseModel):
    x: float
    y: float
    z: float = 0.0  # Para Unity 3D

class TruckData(BaseModel):
    id: int
    position: Position
    load: int
    capacity: int
    load_percentage: float
    status: str

class ContainerData(BaseModel):
    id: int
    position: Position
    current_fill: int
    capacity: int
    fill_percentage: float
    status: str
    is_critical: bool
    is_overflowing: bool

class SimulationData(BaseModel):
    step: int
    trucks: List[TruckData]
    containers: List[ContainerData]
    total_trash_collected: int
    efficiency: float
    critical_containers: int
    timestamp: float

class SimulationConfig(BaseModel):
    steps: int = 1000
    capacity: int = 1000
    epsilon: float = 0.1
    alpha: float = 0.1
    gamma: float = 0.9
    container_limit: int = 75
    population_density: float = 0.1
    simulation_speed: float = 1.0  # Velocidad de la simulación (pasos por segundo)

# Variables globales para manejar la simulación
current_simulation = None
simulation_thread = None
simulation_running = False
simulation_data_history = []
current_step = 0

def convert_position_to_3d(position_2d, agent_type="truck"):
    """Convierte posición 2D del modelo a coordenadas 3D para Unity"""
    x, y = position_2d
    # Escalar las posiciones para Unity (puedes ajustar estos valores)
    scale = 5.0  # Espaciado entre posiciones
    
    # Y es altura en Unity, mantenemos en 0 para objetos en el suelo
    z_offset = 0.5 if agent_type == "truck" else 0.0  # Camiones ligeramente elevados
    
    return Position(
        x=x * scale,
        y=z_offset,
        z=y * scale
    )

def get_truck_status(truck):
    """Determina el estado del camión basado en su carga"""
    load_percentage = (truck.load / truck.capacity) * 100
    if load_percentage >= 90:
        return "full"
    elif load_percentage >= 50:
        return "half_full"
    elif truck.load > 0:
        return "collecting"
    else:
        return "empty"

def get_container_status(container):
    """Determina el estado del contenedor basado en su llenado"""
    if container.is_overflowing():
        return "overflowing"
    elif container.is_critical():
        return "critical"
    elif container.current_fill >= 0.7 * container.capacity:
        return "medium"
    else:
        return "normal"

def extract_simulation_data(model, step_number):
    """Extrae los datos de la simulación en el formato requerido"""
    
    # Datos de camiones
    trucks_data = []
    for i, truck in enumerate(model.trucks):
        trucks_data.append(TruckData(
            id=i,
            position=convert_position_to_3d(truck.position, "truck"),
            load=truck.load,
            capacity=truck.capacity,
            load_percentage=(truck.load / truck.capacity) * 100,
            status=get_truck_status(truck)
        ))
    
    # Datos de contenedores
    containers_data = []
    for i, container in enumerate(model.containers):
        containers_data.append(ContainerData(
            id=i,
            position=convert_position_to_3d(container.position, "container"),
            current_fill=container.current_fill,
            capacity=container.capacity,
            fill_percentage=(container.current_fill / container.capacity) * 100,
            status=get_container_status(container),
            is_critical=container.is_critical(),
            is_overflowing=container.is_overflowing()
        ))
    
    # Calcular estadísticas
    total_trash_collected = sum(truck.load for truck in model.trucks)
    total_trash_in_system = sum(container.current_fill for container in model.containers) + total_trash_collected
    efficiency = (total_trash_collected / total_trash_in_system * 100) if total_trash_in_system > 0 else 0
    critical_containers = sum(1 for container in model.containers if container.is_critical())
    
    return SimulationData(
        step=step_number,
        trucks=trucks_data,
        containers=containers_data,
        total_trash_collected=total_trash_collected,
        efficiency=efficiency,
        critical_containers=critical_containers,
        timestamp=time.time()
    )

def run_simulation_thread(config: SimulationConfig):
    """Ejecuta la simulación en un hilo separado"""
    global current_simulation, simulation_running, simulation_data_history, current_step
    
    # Configurar parámetros
    parameters = {
        'steps': config.steps,
        'capacity': config.capacity,
        'epsilon': config.epsilon,
        'alpha': config.alpha,
        'gamma': config.gamma,
        'container_limit': config.container_limit,
        'population_density': config.population_density
    }
    
    # Crear el modelo
    current_simulation = GarbageEnvironment(parameters)
    simulation_data_history = []
    current_step = 0
    
    # Guardar estado inicial
    initial_data = extract_simulation_data(current_simulation, 0)
    simulation_data_history.append(initial_data)
    
    # Ejecutar simulación paso a paso
    step_delay = 1.0 / config.simulation_speed
    
    for step in range(1, config.steps + 1):
        if not simulation_running:
            break
            
        # Ejecutar un paso de la simulación
        current_simulation.step()
        current_step = step
        
        # Extraer datos del paso actual
        step_data = extract_simulation_data(current_simulation, step)
        simulation_data_history.append(step_data)
        
        # Mantener solo los últimos 100 pasos en memoria
        if len(simulation_data_history) > 100:
            simulation_data_history.pop(0)
        
        # Esperar según la velocidad configurada
        time.sleep(step_delay)
    
    simulation_running = False

@app.get("/")
async def root():
    """Endpoint de prueba"""
    return {"message": "Garbage Collection Simulation API", "status": "running"}

@app.post("/simulation/start")
async def start_simulation(config: SimulationConfig = SimulationConfig()):
    """Inicia una nueva simulación"""
    global simulation_thread, simulation_running, current_simulation
    
    # Detener simulación anterior si existe
    if simulation_running:
        simulation_running = False
        if simulation_thread and simulation_thread.is_alive():
            simulation_thread.join(timeout=2.0)
    
    # Iniciar nueva simulación
    simulation_running = True
    simulation_thread = threading.Thread(target=run_simulation_thread, args=(config,))
    simulation_thread.daemon = True
    simulation_thread.start()
    
    return {
        "message": "Simulation started successfully", 
        "config": config.dict(),
        "status": "running"
    }

@app.post("/simulation/stop")
async def stop_simulation():
    """Detiene la simulación actual"""
    global simulation_running
    
    if simulation_running:
        simulation_running = False
        return {"message": "Simulation stopped", "status": "stopped"}
    else:
        return {"message": "No simulation running", "status": "idle"}

@app.get("/simulation/status")
async def get_simulation_status():
    """Obtiene el estado actual de la simulación"""
    global simulation_running, current_step, current_simulation
    
    if current_simulation is None:
        return {"status": "not_started", "step": 0}
    
    return {
        "status": "running" if simulation_running else "stopped",
        "current_step": current_step,
        "total_trucks": len(current_simulation.trucks),
        "total_containers": len(current_simulation.containers)
    }

@app.get("/simulation/data/current", response_model=SimulationData)
async def get_current_simulation_data():
    """Obtiene los datos actuales de la simulación"""
    global current_simulation, current_step, simulation_data_history
    
    if current_simulation is None:
        raise HTTPException(status_code=404, detail="No simulation running")
    
    if simulation_data_history:
        return simulation_data_history[-1]
    else:
        # Si no hay datos en el historial, generar datos actuales
        return extract_simulation_data(current_simulation, current_step)

@app.get("/simulation/data/step/{step_number}", response_model=SimulationData)
async def get_simulation_data_by_step(step_number: int):
    """Obtiene los datos de un paso específico de la simulación"""
    global simulation_data_history
    
    if not simulation_data_history:
        raise HTTPException(status_code=404, detail="No simulation data available")
    
    # Buscar el paso específico
    for data in simulation_data_history:
        if data.step == step_number:
            return data
    
    raise HTTPException(status_code=404, detail=f"Step {step_number} not found")

@app.get("/simulation/data/history")
async def get_simulation_history(last_n: Optional[int] = 10):
    """Obtiene el historial de los últimos N pasos de la simulación"""
    global simulation_data_history
    
    if not simulation_data_history:
        raise HTTPException(status_code=404, detail="No simulation data available")
    
    # Retornar los últimos N pasos
    return simulation_data_history[-last_n:] if last_n else simulation_data_history

@app.get("/simulation/config/default", response_model=SimulationConfig)
async def get_default_config():
    """Obtiene la configuración por defecto de la simulación"""
    return SimulationConfig()

# Endpoint especial para Unity - datos optimizados
@app.get("/unity/simulation/data")
async def get_unity_simulation_data():
    """Endpoint optimizado para Unity con datos simplificados"""
    global current_simulation, current_step, simulation_data_history
    
    if current_simulation is None:
        raise HTTPException(status_code=404, detail="No simulation running")
    
    if not simulation_data_history:
        raise HTTPException(status_code=404, detail="No simulation data available")
    
    current_data = simulation_data_history[-1]
    
    # Formato simplificado para Unity
    unity_data = {
        "step": current_data.step,
        "trucks": [
            {
                "id": truck.id,
                "position": {"x": truck.position.x, "y": truck.position.y, "z": truck.position.z},
                "load_percentage": truck.load_percentage,
                "status": truck.status
            }
            for truck in current_data.trucks
        ],
        "containers": [
            {
                "id": container.id,
                "position": {"x": container.position.x, "y": container.position.y, "z": container.position.z},
                "fill_percentage": container.fill_percentage,
                "status": container.status
            }
            for container in current_data.containers
        ],
        "simulation_stats": {
            "efficiency": current_data.efficiency,
            "critical_containers": current_data.critical_containers,
            "is_running": simulation_running
        }
    }
    
    return unity_data

if __name__ == "__main__":
    import uvicorn
    print("🚀 Starting Garbage Collection Simulation API Server...")
    print("📡 Access the API at: http://localhost:8000")
    print("📋 API Documentation at: http://localhost:8000/docs")
    uvicorn.run(app, host="0.0.0.0", port=8000)
