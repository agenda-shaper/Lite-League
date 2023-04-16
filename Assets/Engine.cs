using System;
using System.Runtime.InteropServices;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class Boost {
    public bool isBig;
    public bool isActive;
    public float coolDown;
    public GameObject gameObject;
}

public class Car {
    public int id;
    public int team;
    public float speed;
    public float boost;
    public bool isSupersonic = false;
    public bool isBoosting = false;
    public GameObject gameObject;
    public GameObject boostObject;

    public Car(int id, int team, GameObject carObject) {
        this.id = id;
        this.team = team;
        this.gameObject = carObject;
        this.boostObject = carObject.transform.Find("BoostParticles").gameObject;
    }
}

public class Arena {
    public int mainCarId;
    public GameObject arenaObject;
    public Dictionary<int, Car> carDict = new Dictionary<int, Car>();

    public Arena(GameObject arenaObject) {
        this.arenaObject = arenaObject;
    }
    
    public void addCar(Car car) {
        this.carDict.Add(car.id, car);
    }
    
    public Car getCarById(int id) {
        if (this.carDict.TryGetValue(id, out Car car)) {
            return car;
        } else {
            Debug.Log("Car with id " + id + " not found in car dictionary");
            return null;
        }
    }
}

public class Engine : MonoBehaviour
{
    [DllImport("LiteLeague.dll")]
    private static extern IntPtr createArena(int[] GameInfo);
    [DllImport("LiteLeague.dll")]
    private static extern IntPtr GetFieldInfo(IntPtr arenaPtr);
    [DllImport("LiteLeague.dll")]
    private static extern IntPtr GetGameTickPacket(IntPtr arenaPtr);
    [DllImport("LiteLeague.dll")]
    private static extern void startGame(IntPtr arenaPtr);
    [DllImport("LiteLeague.dll")]
    private static extern void cleanUpEverything(IntPtr arenaPtr);
    
    [SerializeField] private GameObject arenaObject;
    [SerializeField] private GameObject ballObject;
    [SerializeField] private GameObject carObject;
    [SerializeField] private GameObject cameraObject;
    [SerializeField] private GameObject smallBoostPadObject;
    [SerializeField] private GameObject bigBoostPadObject;
    [SerializeField] private GameObject boostAmountLeftObject;
    [SerializeField] private GameObject orangeScoreObject;
    [SerializeField] private GameObject blueScoreObject;

    private IntPtr arenaPtr;
    private IntPtr GameTickPacketPtr;
    private int gtpacketSize;
    private float[] GameTickPacket;
    private Arena arena;

    private Thread engineThread;

    private bool ballCamEnabled = false;
    private float cameraDistance = 270f;
    private float cameraHeight = 100f;
    private float cameraAngle = 15f;
    private int num_cars = 2;


    // Start is called before the first frame update
    void Start()
    {
        Debug.Log("Starting Game");
        Time.fixedDeltaTime = 1f / 120f; // fixed fps game engine

        int[] GameInfo = new int[] {
            num_cars, // player count
            0, // 1st player type (main-player)
            0, // 1st player team (BLUE)
            2, // 2nd player type (bot-nexto)
            1 // 2nd player team (ORANGE)
        };
        
        try {
            arenaPtr = createArena(GameInfo);

            arena = new Arena(arenaObject);

        } catch (Exception e) {
            Debug.LogError("Error creating arena: " + e.Message);
        }

        try {

            int boostsAmount = 34;
            int size = boostsAmount * 4 + num_cars*2 + 2;
            float[] FieldInfo = new float[size];
            Marshal.Copy(GetFieldInfo(arenaPtr), FieldInfo, 0, size);
            for (int i = 0; i < boostsAmount; i++)
            {
                GameObject clonedObject;
                if (FieldInfo[i*4] != 0){
                    clonedObject = Instantiate(bigBoostPadObject);
                    clonedObject.transform.parent = bigBoostPadObject.transform.parent;
                } else {
                    clonedObject = Instantiate(smallBoostPadObject);
                    clonedObject.transform.parent = smallBoostPadObject.transform.parent;
                }

                // Position the cloned object
                Vector3 Boostposition = new Vector3(FieldInfo[i*4+2], FieldInfo[i*4+3], FieldInfo[i*4+1]);
                clonedObject.transform.position = Boostposition;
            }

            for (int i = 0; i < num_cars; i++) {
                GameObject carObj = Instantiate(carObject);
                carObj.transform.SetParent(arena.arenaObject.transform);
                Car car = new Car((int)FieldInfo[boostsAmount * 4 + i*2],(int)FieldInfo[boostsAmount * 4 + i*2 + 1], carObj);
                arena.addCar(car);                      
            }
            gtpacketSize = (int)FieldInfo[boostsAmount*4 + num_cars*2];
            GameTickPacket = new float[gtpacketSize];
            GameTickPacketPtr = GetGameTickPacket(arenaPtr);

            arena.mainCarId =(int)FieldInfo[boostsAmount*4 + num_cars*2 + 1];
        }
        catch (Exception e) {
            Debug.LogError("Error initialising FieldData: " + e.Message);
        }

        //Debug.Log(string.Join(", ", FieldInfo));
        
        engineThread = new Thread(RunEngine);
        engineThread.Start();        
    }


    private void RunEngine()
    {
        try {
            startGame(arenaPtr);
        }
        catch (Exception e) {
            Debug.LogError("Error staarting game: " + e.Message);
        }
    }

    // Rendering 120 fps
    void FixedUpdate() {        
        try {
            Marshal.Copy(GameTickPacketPtr, GameTickPacket, 0, gtpacketSize); // read new data
            int num_boosts = 34;

            for (int i = 0; i < num_cars; i++) {

                float CarID = GameTickPacket[i * 16 + 15];
                if (float.IsNaN(CarID) || float.IsInfinity(CarID) || CarID <= 0) {
                    //Debug.Log(CarID);
                    //Debug.Log(string.Join(", ", GameTickPacket));  // first frame uninitialized GameTickPacket prob because of 120tps timing in the cpp engine
                    return;
                }
                Car car = arena.getCarById((int)GameTickPacket[i * 16 + 15]);
                Vector3 carPos = new Vector3(GameTickPacket[i * 16 + 1], GameTickPacket[i * 16 + 2], GameTickPacket[i * 16]);
                car.gameObject.transform.position = carPos;


                Vector3 velocity = new Vector3(GameTickPacket[i * 16 + 3], GameTickPacket[i * 16 + 4], GameTickPacket[i * 16 + 5]);
                car.speed = velocity.magnitude;
                //Debug.Log(car.speed);


                Quaternion carRotation = new Quaternion(GameTickPacket[i * 16 + 7], GameTickPacket[i * 16 + 8], GameTickPacket[i * 16 + 6], GameTickPacket[i * 16 + 9]);
                car.gameObject.transform.rotation = carRotation;      


                car.boost = GameTickPacket[i * 16 + 10];
                car.isBoosting = GameTickPacket[i * 16 + 11] == 1.0f;
                if (car.isBoosting) {
                    car.boostObject.GetComponent<Renderer>().enabled = true;
                } else {
                    car.boostObject.GetComponent<Renderer>().enabled = false;
                }

                if(GameTickPacket[i * 16 + 12] == 1.0f){
                    //Debug.Log("jump");
                }
                car.isSupersonic = GameTickPacket[i * 16 + 13] == 1.0f;

                //GameTickPacket[i * 16 + 14] = static_cast<float>(Car->team);
            }


            Car mainCar = arena.getCarById(arena.mainCarId);     

            boostAmountLeftObject.GetComponent<Text>().text = mainCar.boost.ToString(); 

            if (ballCamEnabled)
            {
                Vector3 direction = mainCar.gameObject.transform.position - ballObject.transform.position;
                float distance = direction.magnitude + cameraDistance*0.6f; // camera distance here
                Vector3 normalizedDirection = direction.normalized;
                Vector3 targetPosition = ballObject.transform.position + normalizedDirection * distance;
                float originalHeight = targetPosition.y;
                targetPosition += new Vector3(0f, cameraHeight + Mathf.Max(0f, -originalHeight), 0f); // camera height here  
                // also subsctract the distance thats in the floor

                cameraObject.transform.position = targetPosition; //Vector3.Lerp(cameraObject.transform.position, targetPosition, 0.8f);

                Quaternion targetRotation = Quaternion.LookRotation(ballObject.transform.position - cameraObject.transform.position);

                // Add downward angle to target position
                Quaternion downwardAngle = Quaternion.Euler(new Vector3(cameraAngle, 0, 0));
                targetRotation *= downwardAngle;

                // Lerp towards desired rotation later implement
                cameraObject.transform.rotation = targetRotation;
            } else {
                cameraObject.transform.position = mainCar.gameObject.transform.TransformPoint(new Vector3(0f, 0f, -cameraDistance*0.6f));
                Quaternion targetRotation = Quaternion.LookRotation(mainCar.gameObject.transform.position - cameraObject.transform.position);
                float originalHeight = cameraObject.transform.position.y;
                cameraObject.transform.position += new Vector3(0f, cameraHeight + Mathf.Max(0f, -originalHeight), 0f); // camera height here
                Quaternion downwardAngle = Quaternion.Euler(new Vector3(cameraAngle, 0, 0));
                targetRotation *= downwardAngle;
                cameraObject.transform.rotation = targetRotation;
            }

            Vector3 ballPos = new Vector3(GameTickPacket[num_cars * 16 + num_boosts * 2 + 1], GameTickPacket[num_cars * 16 + num_boosts * 2 + 2], GameTickPacket[num_cars * 16 + num_boosts*2]);
            ballObject.transform.position = ballPos;


            Quaternion ballRot = new Quaternion(GameTickPacket[num_cars * 16 + num_boosts * 2 + 4], GameTickPacket[num_cars * 16 + num_boosts * 2 + 5], GameTickPacket[num_cars * 16 + num_boosts * 2 + 3], GameTickPacket[num_cars * 16 + num_boosts * 2 + 6]);
            ballObject.transform.rotation = ballRot;
            //Debug.Log("blue: "+GameTickPacket[gtpacketSize-2]+ " orange: " + GameTickPacket[gtpacketSize-1]);
            orangeScoreObject.GetComponent<Text>().text = GameTickPacket[gtpacketSize-1].ToString(); 
            blueScoreObject.GetComponent<Text>().text = GameTickPacket[gtpacketSize-2].ToString(); 
        }
        catch (Exception e) {
            Debug.LogError("Error rendering frame: " + e.Message);
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Toggle ballcam on/off when space is pressed
        if (Input.GetKeyDown(KeyCode.Space))
        {
            ballCamEnabled = !ballCamEnabled;
        }
    }

    void OnApplicationQuit()
    {
        try {
            // Clean up resources
            cleanUpEverything(arenaPtr);

            // Make sure the engine thread is stopped
            engineThread.Join();
            
            arenaPtr = IntPtr.Zero; // clear c# pointers
            GameTickPacketPtr = IntPtr.Zero;
        }
        catch (Exception e) {
            Debug.LogError("Error cleaning up: " + e.Message);
        }
        Debug.Log("exit");
    }
}