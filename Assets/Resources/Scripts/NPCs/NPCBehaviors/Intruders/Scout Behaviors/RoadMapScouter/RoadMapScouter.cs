using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

public class RoadMapScouter : Scouter
{
    private Intruder _intruder;
    private RMScouterParams _rmScouterParams;

    public bool showIntruderGoal;
    private HidingSpot _currentGoalHs;

    // Road map of the level
    public bool showRoadMap;
    private RoadMap _roadMap;

    // Predicted trajectories of guards
    public bool showProjectedTrajectories;
    private RMTrajectoryProjector _trajectoryProjector;

    public bool showRoadMapEndNodes;

    // List of the closest way points to the destination
    private List<RoadMapNode> _closestWpsToDestination;

    public bool showAvailableHidingSpots;

    // List of hiding spots available for the intruder to choose from.
    private List<HidingSpot> _availableSpots;
    private SpotsNeighbourhoods _neighbourhoodType;

    private float _maxSafeRisk;

    // A dictionary of the riskiest spots by each guard on the intruders current path
    public bool showRiskSpots;
    private RMRiskEvaluator _riskEvaluator;

    private RMScoutPathFinder _pathFinder;

    private RMSDecisionMaker _decisionMaker;

    public static RoadMapScouter Instance;

    // The total distance the intruder crossed
    [SerializeField] private float _crossedDistance;


    public override void Initiate(MapManager mapManager, Session session)
    {
        Instance = this;

        base.Initiate(mapManager, session);
        
        _rmScouterParams = (RMScouterParams) session.IntruderBehaviorParams.scouterParams;

        _closestWpsToDestination = new List<RoadMapNode>();
        _availableSpots = new List<HidingSpot>();

        _trajectoryProjector = new RMTrajectoryProjector();
        _trajectoryProjector.Initiate(_rmScouterParams.trajectoryType,
            _rmScouterParams.fovProjectionMultiplier);

        _roadMap = mapManager.GetRoadMap();
        _neighbourhoodType = _rmScouterParams.spotsNeighbourhood;

        _riskEvaluator = gameObject.AddComponent<RMRiskEvaluator>();
        _riskEvaluator.Initiate();

        _pathFinder = new RMScoutPathFinder();

        _decisionMaker = new RMSDecisionMaker();
        _decisionMaker.Initiate(_rmScouterParams.goalPriority, _rmScouterParams.safetyPriority);

        _maxSafeRisk = _rmScouterParams.maxRiskAsSafe;

        // showAvailableHidingSpots = true;
        // showRiskSpots = true;
        // showProjectedTrajectories = true;
        // showRoadMapEndNodes = true;
        // showIntruderGoal = true;
        // showRoadMap = true;
    }


    public override void Begin()
    {
        base.Begin();
        _riskEvaluator.Clear();
        _intruder = NpcsManager.Instance.GetIntruders()[0];
        _crossedDistance = _intruder.GetTravelledDistance();
    }

    public override void Refresh(GameType gameType)
    {
        List<Guard> guards = NpcsManager.Instance.GetGuards();
        Intruder intruder = NpcsManager.Instance.GetIntruders()[0];

        _trajectoryProjector.SetGuardTrajectories(_roadMap, guards);

        _riskEvaluator.UpdateCurrentRisk(_roadMap);

        if (!intruder.IsBusy()) PlanPath(intruder, gameType);

        // Abort the current path if it is too risky
        _riskEvaluator.CheckPathRisk(_rmScouterParams.pathCancel, _roadMap, intruder, guards, ref _availableSpots,
            ref _currentGoalHs);
    }

    private void PlanPath(Intruder intruder, GameType gameType)
    {
        Vector2? goal = GetDestination(gameType);

        _availableSpots.Clear();
        PathFindToDestination(goal, _maxSafeRisk);

        EvaluateSpots(intruder, goal);

        HidingSpot bestHs = null;
        // Get a new destination for the intruder
        while (!intruder.IsBusy() && _availableSpots.Count > 0)
        {
            bestHs =
                _decisionMaker.GetBestSpot(_availableSpots, _riskEvaluator.GetRisk(), _maxSafeRisk);

            if (Equals(bestHs, null)) break;

            List<Vector2> path = intruder.GetPath();
            float maxPathRisk = RMThresholds.GetMaxPathRisk(_rmScouterParams.thresholdType);

            _pathFinder.GetShortestPath(_roadMap, intruder.GetTransform().position, bestHs, ref path,
                maxPathRisk);

            _availableSpots.Remove(bestHs);
        }

        if (!Equals(bestHs, null)) _currentGoalHs = bestHs;
    }

    /// <summary>
    /// Try to find a path to the destination, if that fails then provide possible hiding spots that are closer to the destination
    /// </summary>
    /// <param name="destination"></param>
    /// <param name="minSafeRisk"></param>
    private void PathFindToDestination(Vector2? destination, float minSafeRisk)
    {
        float maxDistance = PathFinding.Instance.longestShortestPath * 0.3f;
        int numOfPossibleRmNodes = 8;
        float maxSearchRisk = RMThresholds.GetMaxSearchRisk(_rmScouterParams.thresholdType);
        bool doAstar = _riskEvaluator.GetRisk() <= minSafeRisk && !Equals(destination, null);

        List<Vector2> path = _intruder.GetPath();
        _pathFinder.GetClosestPointToGoal(_roadMap, _intruder.GetTransform().position,
            destination.Value, numOfPossibleRmNodes, ref _closestWpsToDestination,
            ref path, maxSearchRisk, doAstar, maxDistance);

        // If there is no path to the goal, then check the populate possible hiding spots
        if (_intruder.IsBusy()) return;

        foreach (var wp in _closestWpsToDestination)
            FillAvailableSpots(wp.GetPosition());

        // EditorApplication.isPaused = true;
    }

    private void FillAvailableSpots(Vector2 position)
    {
        switch (_neighbourhoodType)
        {
            case SpotsNeighbourhoods.LineOfSight:
                HidingSpot closestHidingSpot =
                    _HsC.GetClosestHidingSpotToPosition(position);

                if (Equals(closestHidingSpot, null)) return;
                // _HsC.AddAvailableSpots(closestHidingSpot, ref _availableSpots);
                _HsC.AddRandomSpots(closestHidingSpot, ref _availableSpots, 4);
                break;

            case SpotsNeighbourhoods.Grid:
                int numberOfAdjacentCell = RMThresholds.GetSearchDepth(_rmScouterParams.thresholdType);
                _HsC.AddHidingSpots(ref _availableSpots, position, numberOfAdjacentCell);
                break;
        }
    }


    private void EvaluateSpots(Intruder intruder, Vector2? goal)
    {
        foreach (var hs in _availableSpots.Where(hs => !hs.IsAlreadyChecked()))
        {
            SetRiskValue(hs);
            SetGoalUtility(hs, goal);
            SetCostUtility(intruder, hs);
            SetGuardsProximityUtility(hs, NpcsManager.Instance.GetGuards());
            SetOcclusionUtility(hs, NpcsManager.Instance.GetGuards());
        }
    }

    private void SetRiskValue(HidingSpot hs)
    {
        float RANGE = Properties.GetFovRadius(NpcType.Guard);

        float minSqrMag = Mathf.Infinity;
        RoadMapNode closestPossibleGuardPos = null;

        float highestRisk = Mathf.NegativeInfinity;
        RoadMapNode riskestNode = null;

        foreach (var p in _roadMap.GetPossibleGuardPositions())
        {
            bool isVisible =
                GeometryHelper.IsCirclesVisible(hs.Position, p.GetPosition(), Properties.NpcRadius, "Wall");
            if (!isVisible) continue;

            Vector2 offset = hs.Position - p.GetPosition();
            float sqrMag = offset.sqrMagnitude;

            if (sqrMag > RANGE * RANGE) continue;

            if (sqrMag < minSqrMag)
            {
                minSqrMag = sqrMag;
                closestPossibleGuardPos = p;
            }

            if (highestRisk < p.GetProbability())
            {
                highestRisk = p.GetProbability();
                riskestNode = p;
            }
        }

        hs.ClosestRMGuardNode = closestPossibleGuardPos;

        if (Equals(hs.ClosestRMGuardNode, null))
        {
            hs.Risk = 0f;
            return;
        }


        if (hs.ClosestRMGuardNode.distanceFromGuard == 0f)
            hs.Risk = 0.1f;
        else
            hs.Risk = closestPossibleGuardPos.GetProbability();
    }


    private bool didIntruderTravel()
    {
        float distanceThreshold = 3f;
        if (_intruder.GetTravelledDistance() - _crossedDistance >= distanceThreshold)
        {
            _crossedDistance = _intruder.GetTravelledDistance();
            return true;
        }

        return false;
    }


    private bool IsPointFrontNpc(NPC npc, Vector2 point)
    {
        float thresholdCosine = 0.5f;
        Vector2 frontVector = npc.GetDirection();
        Vector2 npcToPoint = point - (Vector2) npc.GetTransform().position;

        float dotProduct = Vector2.Dot(frontVector, npcToPoint);
        return dotProduct > thresholdCosine;
    }


    private void SetGoalUtility(HidingSpot hs, Vector2? goal)
    {
        if (Equals(goal, null))
        {
            hs.GoalUtility = 1f;
            return;
        }

        float distanceToGoal = PathFinding.Instance.GetShortestPathDistance(hs.Position, goal.Value);
        float utilityToGoal = 1f - distanceToGoal / PathFinding.Instance.longestShortestPath;
        hs.GoalUtility = utilityToGoal;
    }


    private void SetCostUtility(Intruder intruder, HidingSpot hs)
    {
        float distanceToDestination =
            PathFinding.Instance.GetShortestPathDistance(intruder.GetTransform().position, hs.Position);

        float cost = 1f - distanceToDestination / PathFinding.Instance.longestShortestPath;
        hs.CostUtility = cost;
    }


// Get the utility for being away from guards
    private void SetGuardsProximityUtility(HidingSpot hs, List<Guard> guards)
    {
        float proximityUtility = 0f;
        float denominator = PathFinding.Instance.longestShortestPath;

        foreach (var guard in guards)
        {
            float distanceToHidingspot =
                PathFinding.Instance.GetShortestPathDistance(hs.Position, guard.GetTransform().position);

            float normalizedDistance = distanceToHidingspot / denominator;

            if (proximityUtility < normalizedDistance)
                proximityUtility = normalizedDistance;
        }

        hs.GuardProximityUtility = proximityUtility;
    }

    private void SetOcclusionUtility(HidingSpot hs, List<Guard> guards)
    {
        float fovRadius = Properties.GetFovRadius(NpcType.Guard);

        float utility = 0f;

        int guardsInRange = 0;
        int visibleGuards = 0;

        foreach (var guard in guards)
        {
            Vector2 offset = (Vector2) guard.GetTransform().position - hs.Position;
            float sqrMag = offset.sqrMagnitude;

            // Make sure the spot is within FoV
            if (sqrMag > fovRadius * fovRadius) continue;

            guardsInRange++;

            bool isVisible = GeometryHelper.IsCirclesVisible(guard.GetTransform().position, hs.Position,
                Properties.NpcRadius, "Wall");

            if (!isVisible) continue;

            visibleGuards++;
        }

        if (guardsInRange == 0)
            utility = 0f;
        else
            utility = (float) visibleGuards / guardsInRange;

        hs.OcclusionUtility = 1f - utility;
    }

    public void OnDrawGizmos()
    {
        base.OnDrawGizmos();

        if (showRoadMapEndNodes && !Equals(_closestWpsToDestination, null))
        {
            Gizmos.color = Color.green;
            foreach (var n in _closestWpsToDestination)
            {
                Gizmos.DrawSphere(n.GetPosition(), 0.25f);

#if UNITY_EDITOR
                Handles.Label(n.GetPosition() + 0.5f * Vector2.down, n.GetProbability().ToString());
#endif
            }
        }

        if (showIntruderGoal && !Equals(_currentGoalHs, null))
        {
            Gizmos.color = Color.green;
            _currentGoalHs.Draw();
        }


        if (showRoadMap)
            _roadMap.DrawWalkableRoadmap(true);

        if (showRiskSpots)
            _riskEvaluator.Draw();

        if (showProjectedTrajectories)
        {
            Gizmos.color = Color.red;

            // Draw the lines
            foreach (var psbTrac in _trajectoryProjector.GetTrajectories())
                psbTrac.Draw();

            // Draw the nodes
            foreach (var t in _roadMap.GetPossibleGuardPositions())
            {
                float value = Mathf.Round(t.GetProbability() * 100f) * 0.01f;
                t.Draw(value.ToString());
            }
        }

        Gizmos.color = Color.yellow;
        if (showAvailableHidingSpots && !Equals(_availableSpots, null))
            foreach (var s in _availableSpots)
            {
                s.Draw();
            }
    }
}

public class RMScouterParams : ScouterParams
{
    public readonly SpotsNeighbourhoods spotsNeighbourhood;

    /// <summary>
    /// Path Cancelling method
    /// </summary>
    public readonly PathCanceller pathCancel;

    public readonly RiskThresholdType thresholdType;

    public readonly TrajectoryType trajectoryType;

    public readonly float maxRiskAsSafe;
    
    public readonly GoalPriority goalPriority;

    public readonly SafetyPriority safetyPriority;

    public readonly float fovProjectionMultiplier;

    public RMScouterParams(SpotsNeighbourhoods spotsNeighbourhood, PathCanceller pathCancel, RiskThresholdType thresholdType, TrajectoryType trajectoryType, float maxRiskAsSafe, GoalPriority goalPriority, SafetyPriority safetyPriority, float fovProjectionMultiplier)
    {
        this.spotsNeighbourhood = spotsNeighbourhood;
        this.pathCancel = pathCancel;
        this.thresholdType = thresholdType;
        this.trajectoryType = trajectoryType;
        this.maxRiskAsSafe = maxRiskAsSafe;
        this.goalPriority = goalPriority;
        this.safetyPriority = safetyPriority;
        this.fovProjectionMultiplier = fovProjectionMultiplier;
    }

    public override string ToString()
    {
        string output = "";
        string sep = "_";

        output += spotsNeighbourhood;
        output += sep;

        output += pathCancel;
        output += sep;

        output += thresholdType;
        output += sep;
        
        output += fovProjectionMultiplier;
        output += sep;            
            
        output += trajectoryType;
        output += sep;

        output += goalPriority;
        output += sep;

        output += safetyPriority;
        output += sep;

        output += maxRiskAsSafe;
        
        return output;
    }
}

public class PossiblePosition
{
    private Vector2? _position;

    // The NPC this possible position belong to
    public NPC npc;

    // Distance from the NPC
    public float sqrDistance;

    /// <summary>
    /// Safety Multiplier; the lower it is the closer this point to the guard. It ranges between 0 and 1 
    /// </summary>
    public float safetyMultiplier;

    public float risk;

    public PossiblePosition(Vector2? position, NPC npc)
    {
        _position = position;
        this.npc = npc;
    }

    public PossiblePosition(Vector2 position, NPC npc, float _distance) : this(position, npc)
    {
        _position = position;
        this.npc = npc;
        // distance = _distance;
        // safetyMultiplier = distance / RoadMapScouter.GetProjectionDistance();
    }

    public void SetPosition(Vector2? position)
    {
        _position = position;
    }

    public Vector2? GetPosition()
    {
        return _position;
    }

    public void Draw(string label, Color32 color)
    {
        if (Equals(_position, null)) return;

#if UNITY_EDITOR
        Handles.Label(_position.Value + Vector2.down * 0.5f, label);
#endif
        Gizmos.color = color;
        Gizmos.DrawSphere(_position.Value, 0.2f);
    }
}


public struct LabelSpot
{
    public string label;
    public Vector2 position;

    public LabelSpot(string _label, Vector2 _position)
    {
        label = _label;
        position = _position;
    }
}

// The hiding spot neighbourhood population method 
public enum SpotsNeighbourhoods
{
    // Add hiding spot based on line of sight
    LineOfSight,

    // Add hiding spots based on a grid
    Grid,
    All
}

public enum PathCanceller
{
    DistanceCalculation,

    RiskComparison,

    None
}