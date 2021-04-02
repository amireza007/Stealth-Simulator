﻿using System;
using System.Collections.Generic;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

public class GameManager : MonoBehaviour
{
    // Logging Variables 
    [Header("Logging")] [Tooltip("Log the performance")]
    public Logging loggingMethod;

    [Header("Time")] [Tooltip("Simulation speed")] [Range(1, 20)]
    public int SimulationSpeed;

    // The path to the stealth area prefab
    private readonly string m_StealthArea = "Prefabs/StealthArea";

    // Active area
    private GameObject m_activeArea;

    // The Scenarios to be executed
    private List<Session> m_Sessions;

    [Tooltip("Rendering")] public bool Render;

    // Location of the data for the game
    public static string DataPath;
    public static string LogsPath = "../../../Logs/";
    public static string MapsDataPath = "MapsData/";
    public static string MapsPath = "Maps/";
    public static string RoadMapsPath = "RoadMaps/";

    // The main camera
    public static Camera MainCamera;

    private void Start()
    {
        m_activeArea = null;
        m_Sessions = new List<Session>();

        // Define the paths for the game
        // Main path
        DataPath = Application.dataPath + "/Data/";
        // Logs path
        LogsPath = DataPath + LogsPath;
        // Map related data paths
        MapsDataPath = DataPath + MapsDataPath;
        MapsPath = MapsDataPath + MapsPath;
        RoadMapsPath = MapsDataPath + RoadMapsPath;

        // Camera
        MainCamera = Camera.main;

        // Load sessions
        LoadSavedSessions();

        // Set the simulation speed
        Time.timeScale = SimulationSpeed;

        // Initiate the containers for path finding.
        PathFinding.Initiate();

        // Load the next session
        LoadNextScenario();
    }

    private void LoadSavedSessions()
    {
        // // Get the path to the sessions records
        // string path = DataPath + "/Sessions.csv";
        //
        // // Load the sessions file
        // var sessionsString = CsvController.ReadString(path);

        var sessionsString = EnumerateSessionData();

        // Split data by lines
        var lines = sessionsString.Split('\n');

        // Each line represents a session
        for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            if (lines[lineIndex].Length > 0)
            {
                // Split the elements
                var data = lines[lineIndex].Split(',');

                // Get the session info
                var sc = new Session(data[0], (Scenario) Enum.Parse(typeof(Scenario), data[1], true),
                    int.Parse(data[9]), int.Parse(data[12]),
                    int.Parse(data[2]), (WorldRepType) Enum.Parse(typeof(WorldRepType), data[3], true), data[4],
                    float.Parse(data[5]));

                // Set the guard behavior
                GuardBehavior guardBehavior = new GuardBehavior(
                    (GuardPatrolPlanner) Enum.Parse(typeof(GuardPatrolPlanner), data[6], true),
                    (GuardChasePlanner) Enum.Parse(typeof(GuardChasePlanner), data[7], true),
                    (GuardSearchPlanner) Enum.Parse(typeof(GuardSearchPlanner), data[8], true));

                // Add the guards
                for (int i = 0; i < int.Parse(data[9]); i++)
                    sc.AddGuard(i + 1, NpcType.Guard, guardBehavior, null,
                        (PathFindingHeursitic) Enum.Parse(typeof(PathFindingHeursitic), data[10], true),
                        (PathFollowing) Enum.Parse(typeof(PathFollowing), data[11], true),
                        null);

                // Add the intruders
                for (int i = 0; i < int.Parse(data[12]); i++)
                    sc.AddIntruder(i + 1, NpcType.Intruder, null,
                        (IntruderPlanner) Enum.Parse(typeof(IntruderPlanner), data[13], true),
                        (PathFindingHeursitic) Enum.Parse(typeof(PathFindingHeursitic), data[14], true),
                        (PathFollowing) Enum.Parse(typeof(PathFollowing), data[15], true),
                        null);

                // Check if the required number of Episodes is logged already or skip if logging is not required.
                if (!PerformanceMonitor.IsLogged(sc) || loggingMethod == Logging.None)
                    m_Sessions.Add(sc);
            }
    }


    private string EnumerateSessionData()
    {
        Dictionary<string, string> maps = new Dictionary<string, string>();
        // Add maps
        // maps.Add("MgsDock", "2");
        // maps.Add("dragon_age_brc202d", "1");
        // maps.Add("dragon_age2", "1");
        // maps.Add("valorant_ascent", "2");
        maps.Add("Boxes", "1");
        maps.Add("AlienIsolation", "4");
        maps.Add("CoD_relative", "0.1");

        List<string> guardsCount = new List<string>() {"2"};

        List<string> intruderPlanners = new List<string>() {"RandomMoving", "Heuristic", "HeuristicMoving"};

        List<string> methods = new List<string>() {"RmPropSimple", "Cheating", "RmPropOccupancyDiffusal"};

        // List<string> methods = new List<string>() {"RmPropOccupancyDiffusal"};

        string sessions =
            "GameCode,Scenario,CoverageResetThreshold,WorldRep,Map,MapScale,GuardPatrolPlanner,GuardChasePlanner,GuardSearchPlanner,GuardsCount,PathFindingHeursitic,PathFollowing,IntudersCount,IntruderPlanner,PathFindingHeursitic,PathFollowing\n";

        foreach (var map in maps)
        foreach (var guardCount in guardsCount)
        foreach (var intruderPlanner in intruderPlanners)
        foreach (var method in methods)
        {
            sessions += ",Chase,100,Continuous,";
            sessions += map.Key + "," + map.Value;
            sessions += ",Stalest,Simple,";
            sessions += method + ",";
            sessions += guardCount;
            sessions += ",EuclideanDst,SimpleFunnel,1,";
            sessions += intruderPlanner;
            sessions += ",EuclideanDst,SimpleFunnel\n";
        }

        return sessions;
    }


    // Create the area and load it with the scenario
    private GameObject CreateArea(Session scenario)
    {
        // Get the area prefab
        var areaPrefab = (GameObject) Resources.Load(m_StealthArea);
        var areaGameObject = Instantiate(areaPrefab, transform, true);

        // Get the script
        var area = areaGameObject.GetComponent<StealthArea>();

        // Load the scenario
        area.InitiateArea(scenario);

        return areaGameObject;
    }

    // Replenish the scenarios
    private void LoadNextScenario()
    {
        if (m_activeArea == null)
        {
            if (m_Sessions.Count > 0)
            {
                m_activeArea = CreateArea(m_Sessions[0]);
                Camera.main.orthographicSize = 10;

                m_Sessions.RemoveAt(0);
            }
            else
            {
#if UNITY_STANDALONE_WIN
                Application.Quit();
#endif

#if UNITY_EDITOR
                EditorApplication.isPlaying = false;
#endif
            }
        }
    }

    // Remove the current area and load the next scenario
    public void RemoveArea(GameObject area)
    {
        Destroy(area);
        m_activeArea = null;
        LoadNextScenario();
    }
}

// Logging modes
public enum Logging
{
    // Save log files locally.
    Local,

    // Upload log files to a server.
    Cloud,

    // No logging.
    None
}

// World Representation Type 
public enum WorldRepType
{
    Continuous,
    Grid
}

// Guard decision maker for patrol
public enum GuardPatrolPlanner
{
    Stalest,
    Random,
    UserInput
}

// Guard decision maker for chasing an intruder
public enum GuardChasePlanner
{
    Simple,
    Intercepting
}

// Guard decision maker for searching for an intruder
public enum GuardSearchPlanner
{
    // Randomly traverse the nodes of the Abstraction graph
    Random,

    // The guards search the road map while propagating the probability of the intruder's presence.
    // The probability is diffused similarly to Damian Isla's implementation
    RmPropOccupancyDiffusal,

    // The probability is simply propagated through the road map.
    RmPropSimple,

    // The guards know the intruder's position at all times.
    Cheating
}

// Intruder behavior 
public enum IntruderPlanner
{
    Random,
    RandomMoving,
    UserInput,
    Heuristic,
    HeuristicMoving
}

// Heuristic for path finding 
public enum PathFindingHeursitic
{
    EuclideanDst
}

// Path following algorithm
public enum PathFollowing
{
    SimpleFunnel
}


public enum NpcType
{
    Guard,
    Intruder
}

// the scenario session will be set in
public enum Scenario
{
    // The session starts with randomly allocating the npcs on the map.
    Normal,

    // The session starts with the intruder, if present, being at a certain distance from one of the guards 
    Chase,

    // The NPCs are Manually set in the map
    Manual
}


// The view of the game based on the perspective
public enum GameView
{
    // The game renders all NPCs at all times.
    Spectator,

    // The game only renders the guards when they are seen by the intruder.
    Intruder,

    // The game only renders the intruder when seen by the guards.
    Guard
}


// Struct for the guard planners
public struct GuardBehavior
{
    public GuardPatrolPlanner patrol;
    public GuardChasePlanner chase;
    public GuardSearchPlanner search;

    public GuardBehavior(GuardPatrolPlanner _patrol, GuardChasePlanner _chase, GuardSearchPlanner _search)
    {
        patrol = _patrol;
        chase = _chase;
        search = _search;
    }
}


[Serializable]
public struct NpcData
{
    // A single source to set NPC IDs
    public static int NpcsCount = 0;
    public int id;

    // The NPC type
    public NpcType npcType;

    // The Planner the guard uses to find its next move
    public GuardBehavior? guardPlanner;

    // Intruder planner
    public IntruderPlanner? intruderPlanner;

    // The A* search heuristic
    public PathFindingHeursitic npcHeuristic;

    // Navmesh following behavior
    public PathFollowing npcPathFollowing;

    // Initial position for the NPC
    public NpcLocation? location;

    public NpcData(int _id, NpcType pNpcType, GuardBehavior? _guardPlanner, IntruderPlanner? _intruderPlanner,
        PathFindingHeursitic pPathFindingHeuristic, PathFollowing pNpcPathFollowing, NpcLocation? _location)
    {
        id = _id;
        npcType = pNpcType;
        guardPlanner = _guardPlanner;
        intruderPlanner = _intruderPlanner;
        npcHeuristic = pPathFindingHeuristic;
        npcPathFollowing = pNpcPathFollowing;
        location = _location;
    }

    public override string ToString()
    {
        var data = "";
        data += npcType + ",";
        data += id + ",";
        data += guardPlanner + ",";
        data += npcHeuristic + ",";
        data += npcPathFollowing;

        return data;
    }
}

public struct NpcLocation
{
    public Vector2? position;
    public float rotation;

    public NpcLocation(Vector2 _position, float _rotation)
    {
        position = _position;
        rotation = _rotation;
    }
}

// Session info
[Serializable]
public struct Session
{
    // Game Code is the scenario for the NPC
    public string gameCode;

    // Session scenario
    public Scenario scenario;

    // World Representation
    public WorldRepType worldRepType;

    // The Threshold at which the covered region is reset 
    public int coveredRegionResetThreshold;

    // Number of guards
    public float guardsCount;

    // Number of Intruders
    public int intruderCount;

    // The map 
    public string map;

    // The map Scale
    public float mapScale;

    // Guards Data
    public List<NpcData> guardsList;

    // Intruders Data
    public List<NpcData> intrudersList;

    public Session(string pGameCode, Scenario pScenario, int pGuardsCount, int pIntruderCount,
        int pCoveredRegionResetThreshold,
        WorldRepType pWorldRepType,
        string pMap,
        float pMapScale = 1f)
    {
        gameCode = pGameCode;
        scenario = pScenario;
        guardsCount = pGuardsCount;
        intruderCount = pIntruderCount;
        coveredRegionResetThreshold = pCoveredRegionResetThreshold;
        worldRepType = pWorldRepType;
        map = pMap;
        guardsList = new List<NpcData>();
        intrudersList = new List<NpcData>();
        mapScale = pMapScale;
    }


    public float GetMapScale()
    {
        return mapScale;
    }

    // Add a NPC to the list
    public void AddGuard(int id, NpcType npcType, GuardBehavior? guardPlanner, IntruderPlanner? intruderPlanner,
        PathFindingHeursitic pathFindingHeuristic, PathFollowing pathFollowing, NpcLocation? npcLocation)
    {
        guardsList.Add(new NpcData(id, npcType, guardPlanner, intruderPlanner, pathFindingHeuristic, pathFollowing,
            npcLocation));
    }

    public void AddIntruder(int id, NpcType npcType, GuardBehavior? guardPlanner, IntruderPlanner? intruderPlanner,
        PathFindingHeursitic pathFindingHeuristic, PathFollowing pathFollowing, NpcLocation? npcLocation)
    {
        intrudersList.Add(new NpcData(id, npcType, guardPlanner, intruderPlanner, pathFindingHeuristic, pathFollowing,
            npcLocation));
    }


    // Add the NPC data
    public List<NpcData> GetGuardsData()
    {
        return guardsList;
    }

    public List<NpcData> GetIntrudersData()
    {
        return intrudersList;
    }

    public override string ToString()
    {
        // Separator
        string sep = " ";

        string sessionInfo = "";

        // Game code
        sessionInfo += gameCode + sep;

        // Man name
        sessionInfo += map + sep;

        // Map scale
        sessionInfo += GetMapScale() + sep;

        // Guard planner 
        sessionInfo += (GetGuardsData().Count > 0 ? GetGuardsData()[0].guardPlanner.Value.search.ToString() : "") + sep;

        // Guard FoV percentage of the longest path in the map
        sessionInfo += Properties.GuardsFovRadiusPercentage + sep;

        // Number of guards
        sessionInfo += guardsCount + sep;

        // Intruder planner 
        sessionInfo += (GetIntrudersData().Count > 0 ? GetIntrudersData()[0].intruderPlanner.Value.ToString() : "") +
                       sep;

        // Intruder's speed percentage to guards
        sessionInfo += Properties.IntruderSpeedPercentage + sep;

        // Length of the episode
        sessionInfo += Properties.EpisodeLength;

        return sessionInfo;
    }
}