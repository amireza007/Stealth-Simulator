using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Vector2 = UnityEngine.Vector2;

public class RoadMapScouter : Scouter
{
    private Intruder _intruder;

    // Road map of the level
    public bool showRoadMap;
    private RoadMap _roadMap;

    // Predicted trajectories of guards
    public bool showProjectedTrajectories;

    // private List<PossibleTrajectory> _possibleTrajectories;
    private RMTrajectoryProjector _trajectoryProjector;

    // List of the closest way points to the destination
    private List<WayPoint> _closestWpsToDestination;

    public bool showAvailableHidingSpots;

    // List of hiding spots available for the intruder to choose from.
    private List<HidingSpot> _availableSpots;

    // A dictionary of the riskiest spots by each guard on the intruders current path
    public bool showRiskSpots;
    private RMRiskEvaluator _riskEvaluator;

    private RMScoutPathFinder _pathFinder;

    private RMSDecisionMaker _decisionMaker;

    // The number of attempts to find the next spot
    private static int _attemptCount = 0;

    // The total distance the intruder crossed
    [SerializeField] private float _crossedDistance;


    public override void Initiate(MapManager mapManager)
    {
        base.Initiate(mapManager);

        _closestWpsToDestination = new List<WayPoint>();
        _availableSpots = new List<HidingSpot>();

        _trajectoryProjector = new RMTrajectoryProjector();
        _trajectoryProjector.Initiate();

        _roadMap = mapManager.GetRoadMap();

        _riskEvaluator = gameObject.AddComponent<RMRiskEvaluator>();
        _riskEvaluator.Initiate();
        _pathFinder = new RMScoutPathFinder();
        _decisionMaker = new RMSDecisionMaker();

        // showAvailableHidingSpots = true;
        showRiskSpots = true;
        // showProjectedTrajectories = true;
        showRoadMap = true;
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

        if (didIntruderTravel()) _attemptCount = 0;

        Vector2? goal = GetDestination(gameType);

        PathFindToDestination(goal, 0f);

        if (!intruder.IsBusy())
        {
            HidingSpot closestHidingSpot = _HsC.GetClosestHidingSpotToPosition(intruder.GetTransform().position);

            _availableSpots.Clear();
            _HsC.AddAvailableSpots(closestHidingSpot, 1, ref _availableSpots);

            // int numberOfAdjacentCell = 12;
            // _availableSpots = _HsC.GetHidingSpots(intruder.GetTransform().position, numberOfAdjacentCell);
        }

        if (_availableSpots.Count > 0) EvaluateSpots(intruder, goal);

        // Get a new destination for the intruder
        while (!intruder.IsBusy() && _availableSpots.Count > 0)
        {
            HidingSpot bestHs =
                _decisionMaker.GetBestSpot(_availableSpots, _riskEvaluator.GetRisk());

            if (Equals(bestHs, null)) return;

            List<Vector2> path = intruder.GetPath();
            float maxPathRisk = RMThresholds.GetMaxPathRisk(intruderBehavior.thresholdType);
            _pathFinder.GetShortestPath(_roadMap, intruder.GetTransform().position, bestHs, ref path,
                maxPathRisk);

            _availableSpots.Remove(bestHs);
        }

        if (!intruder.IsBusy())
        {
            _attemptCount++;
            _attemptCount = Mathf.Min(_attemptCount, RMThresholds.GetMaxAttempts());
            Debug.Log("Failed to find hiding spot");
            return;
        }

        // Abort the current path if it is too risky
        _riskEvaluator.CheckPathRisk(intruderBehavior.pathCancel, _roadMap, intruder, guards);
    }

    public static int GetAttemptsCount()
    {
        return _attemptCount;
    }

    /// <summary>
    /// Try to find a path to the destination, if that fails then provide possible hiding spots that are closer to the destination
    /// </summary>
    /// <param name="destination"></param>
    private void PathFindToDestination(Vector2? destination, float maxRisk)
    {
        if (Equals(destination, null)) return;
        if (_intruder.IsBusy()) return;
        if (_riskEvaluator.GetRisk() > maxRisk) return;

        List<Vector2> path = _intruder.GetPath();

        float maxSearchRisk = RMThresholds.GetMaxSearchRisk(intruderBehavior.thresholdType);
        _pathFinder.GetClosestPointToGoal(_roadMap, _intruder.GetTransform().position,
            destination.Value, 3, ref _closestWpsToDestination,
            ref path, maxSearchRisk);

        _availableSpots.Clear();
        foreach (var wp in _closestWpsToDestination)
        {
            HidingSpot closestHidingSpot = _HsC.GetClosestHidingSpotToPosition(wp.GetPosition());
            _HsC.AddAvailableSpots(closestHidingSpot, 1, ref _availableSpots);
        }
    }


    private Vector2? GetDestination(GameType gameType)
    {
        Vector2? goal = null;

        switch (gameType)
        {
            case GameType.CoinCollection:
                goal = CollectablesManager.Instance.GetGoalPosition(gameType);
                break;

            case GameType.StealthPath:
                break;
        }

        return goal;
    }
    
    private void EvaluateSpots(Intruder intruder, Vector2? goal)
    {
        int i = 0;
        while (i < _availableSpots.Count)
        {
            HidingSpot hs = _availableSpots[i];

            SetRiskValue(hs);
            SetGoalUtility(hs, goal);
            SetCostUtility(intruder, hs);
            SetGuardsProximityUtility(hs, NpcsManager.Instance.GetGuards());
            // SetOcclusionUtility(hs, NpcsManager.Instance.GetGuards());

            i++;
        }
    }

    // private void SetRiskValue(HidingSpot hs)
    // {
    //     float guardFovRadius = Properties.GetFovRadius(NpcType.Guard);
    //
    //     // Get the closest trajectory to this spot
    //     float shortestDistanceToTrajectory = Mathf.Infinity;
    //
    //     PossiblePosition closestPointOnTrajectory = null;
    //     foreach (var trajectory in _trajectoryProjector.GetTrajectories())
    //     {
    //         Vector2? pointOnTrajectory =
    //             GeometryHelper.GetClosetPointOnPath(trajectory.GetPath(), hs.Position, Properties.NpcRadius);
    //
    //         float distance;
    //         Vector2? closestPoint;
    //         if (Equals(pointOnTrajectory, null))
    //         {
    //             closestPoint = trajectory.GetLastPoint();
    //             distance =
    //                 PathFinding.Instance.GetShortestPathDistance(closestPoint.Value, hs.Position);
    //         }
    //         else
    //         {
    //             closestPoint = pointOnTrajectory.Value;
    //             distance = Vector2.Distance(hs.Position, closestPoint.Value);
    //         }
    //
    //
    //         if (distance < shortestDistanceToTrajectory)
    //         {
    //             closestPointOnTrajectory ??= new PossiblePosition(closestPoint.Value, trajectory.npc);
    //
    //             closestPointOnTrajectory.SetPosition(closestPoint.Value);
    //             closestPointOnTrajectory.npc = trajectory.npc;
    //             shortestDistanceToTrajectory = distance;
    //         }
    //     }
    //
    //     hs.ThreateningPosition = closestPointOnTrajectory;
    //
    //     // When there are no threatening positions, it has no risk
    //     if (Equals(hs.ThreateningPosition, null))
    //     {
    //         hs.RiskLikelihood = 0f;
    //         return;
    //     }
    //
    //     // Assign the maximum safety value
    //     float distanceFromBeingSeen = GetGuardProjectionDistance(hs.ThreateningPosition.npc);
    //
    //     // If the hiding position is approx within radius of guard trajectory, then adjust it's risk value.
    //     if (shortestDistanceToTrajectory <= guardFovRadius)
    //     {
    //         float distanceFromGuardToPoint = PathFinding.Instance.GetShortestPathDistance(
    //             closestPointOnTrajectory.npc.GetTransform().position,
    //             closestPointOnTrajectory.GetPosition().Value);
    //
    //         // Subtract the Fov radius so if the hiding position is already within vision it is not safe anymore.
    //         distanceFromBeingSeen = distanceFromGuardToPoint - guardFovRadius;
    //         distanceFromBeingSeen = Mathf.Max(0f, distanceFromBeingSeen);
    //     }
    //
    //     // Get the orientation of the threatening position to the guard.
    //     bool isPointInFront = IsPointFrontNpc(hs.ThreateningPosition.npc, hs.Position);
    //
    //     // The spot is behind the guard
    //     if (distanceFromBeingSeen < 0.01f && !isPointInFront)
    //         distanceFromBeingSeen = GetGuardProjectionDistance(hs.ThreateningPosition.npc) * 0.5f;
    //     hs.RiskLikelihood = 1f - distanceFromBeingSeen / GetGuardProjectionDistance(hs.ThreateningPosition.npc);
    // }


    private void SetRiskValue(HidingSpot hs)
    {
        float minSqrMag = Mathf.Infinity;
        WayPoint closestPossibleGuardPos = null;

        foreach (var p in _roadMap.GetPossibleGuardPositions())
        {
            bool isVisible =
                GeometryHelper.IsCirclesVisible(hs.Position, p.GetPosition(), Properties.NpcRadius, "Wall");

            if (!isVisible) continue;

            Vector2 offset = hs.Position - p.GetPosition();
            float sqrMag = offset.sqrMagnitude;

            if (sqrMag < minSqrMag)
            {
                minSqrMag = sqrMag;
                closestPossibleGuardPos = p;
            }
        }

        float risk = (Equals(closestPossibleGuardPos, null)) ? 0f : closestPossibleGuardPos.GetProbability();

        hs.RiskLikelihood = risk;
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

        float cost = distanceToDestination / PathFinding.Instance.longestShortestPath;
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

// /// <summary>
// /// Get the occlusion value of a hiding spot.
// /// The value is between 0 and 1, it reflects the normalized distance to the closest non occluded guard.
// /// </summary>
// private void SetOcclusionUtility(HidingSpot hs, List<Guard> guards)
// {
//     float utility = 1f;
//     float denominator = PathFinding.Instance.longestShortestPath;
//
//     foreach (var guard in guards)
//     {
//         bool isVisible = GeometryHelper.IsCirclesVisible(hs.Position, guard.GetTransform().position, 0.1f, "Wall");
//
//         if (!isVisible) continue;
//
//         float distanceToHidingspot = Vector2.Distance(hs.Position, guard.GetTransform().position);
//
//         float normalizedDistance = distanceToHidingspot / denominator;
//
//         if (utility > normalizedDistance)
//         {
//             utility = normalizedDistance;
//         }
//     }
//
//     hs.OcclusionUtility = utility;
// }

    public void OnDrawGizmos()
    {
        base.OnDrawGizmos();
        if (showRoadMap)
            _roadMap.DrawWalkableRoadmap();
        if (showRiskSpots)
            _riskEvaluator.Draw(_intruder.GetTransform().position);
        if (showProjectedTrajectories)
        {
            foreach (var psbTrac in _trajectoryProjector.GetTrajectories())
                psbTrac.Draw();

            foreach (var t in _roadMap.GetPossibleGuardPositions())
            {
                float value = Mathf.Round(t.GetProbability() * 100f) * 0.01f;
                t.Draw(value.ToString());
            }
        }

        Gizmos.color = Color.blue;
        if (showAvailableHidingSpots && !Equals(_availableSpots, null))
            foreach (var s in _availableSpots)
            {
                s.Draw();
            }
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