﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Used for the search representation the game 
public class Searcher : MonoBehaviour
{
    private StealthArea m_SA;

    // Variables for path finding
    private List<RoadMapLine> open;
    private List<RoadMapLine> closed;

    private RoadMap m_roadMap;

    private GuardSearchPlanner m_searchType;

    public bool RenderSearchSegments;


    // Properties of the simple propagation method.
    // The minimum probability for a segment to be considered by the guard
    private float m_minSegThreshold = 0.4f;

    private float m_expansionMultiplyer = 0.75f;


    // Time it takes to make a decision for all guards
    public float Updated;

    public void Initiate(StealthArea stealthArea)
    {
        open = new List<RoadMapLine>();
        closed = new List<RoadMapLine>();

        m_SA = stealthArea;
        m_roadMap = stealthArea.roadMap;

        foreach (var npcData in m_SA.GetSessionInfo().GetGuardsData())
        {
            m_searchType = npcData.guardPlanner.Value.search;
            break;
        }

        RenderSearchSegments = true;
    }

    // Move the interception point for the search phase
    public void PlaceSsForSearch(Vector2 position, Vector2 dir)
    {
        // Insert the search segment 
        m_roadMap.CommenceProbabilityFlow(position, dir);
    }


    public void UpdateSearcher(float speed, List<Guard> guards, float timeDelta)
    {
        float timeBefore = Time.realtimeSinceStartup;
        if (m_searchType == GuardSearchPlanner.RmPropSimple)
            UpdateSimpleSearch(speed, guards, timeDelta);
        else if (m_searchType == GuardSearchPlanner.RmPropOccupancyDiffusal)
            UpdateOccupancySearch(speed, guards, timeDelta);
        Updated = (Time.realtimeSinceStartup - timeBefore);
    }


    // The probability is propagated with a factor.
    private void UpdateSimpleSearch(float speed, List<Guard> guards, float timeDelta)
    {
        float maxProbability = 0f;
        // Spread the probability similarly to Third eye crime
        foreach (var line in m_roadMap.GetLines())
        {
            line.PropagateProb(speed, timeDelta);
            line.IncreaseProbability(speed, timeDelta);
            line.ExpandSs(speed * m_expansionMultiplyer, timeDelta);

            CheckSeenSs(guards, line);

            float prob = line.GetSearchSegment().GetProbability();

            if (maxProbability < prob) maxProbability = prob;
        }

        if (maxProbability < m_minSegThreshold)
            NormalizeProbs(maxProbability);
    }

    // The probability is diffused, similar to Third eye crime
    private void UpdateOccupancySearch(float speed, List<Guard> guards, float timeDelta)
    {
        float maxProbability = 0f;

        foreach (var line in m_roadMap.GetLines())
        {
            DiffuseProb(line);
            line.ExpandSs(speed, timeDelta);

            CheckSeenSs(guards, line);

            float prob = line.GetSearchSegment().GetProbability();

            if (maxProbability < prob)
                maxProbability = prob;
        }
        
        NormalizeProbs(maxProbability);
    }

    // Diffusing the probability among neighboring segments 
    // Source: EXPLORATION AND COMBAT IN NETHACK - Johnathan Campbell - Chapter 2.2.1
    public void DiffuseProb(RoadMapLine line)
    {
        SearchSegment sS = line.GetSearchSegment();
        float probabilitySum = 0f;
        int neighborsCount = 0;

        foreach (var con in line.GetWp1Connections())
            if (line != con)
            {
                float normalizedAge = con.GetSearchSegment().GetAge() / 10f;
                normalizedAge = normalizedAge > 1f ? 1f : normalizedAge;
                // float diffuseFactor = Mathf.Lerp(0f, Properties.ProbDiffFac, normalizedAge);
                // probabilitySum += con.GetSearchSegment().GetProbability() * diffuseFactor;
                probabilitySum += con.GetSearchSegment().GetProbability() * Properties.ProbDiffFac;
                neighborsCount++;
            }

        foreach (var con in line.GetWp2Connections())
            if (line != con)
            {
                float normalizedAge = con.GetSearchSegment().GetAge() / 10f;
                normalizedAge = normalizedAge > 1f ? 1f : normalizedAge;
                // float diffuseFactor = Mathf.Lerp(0f, Properties.ProbDiffFac, normalizedAge);
                // probabilitySum += con.GetSearchSegment().GetProbability() * diffuseFactor;
                probabilitySum += con.GetSearchSegment().GetProbability() * Properties.ProbDiffFac;
                neighborsCount++;
            }


        // float newProbability = (1f - Properties.ProbDiffFac) * sS.GetProbability() +
        //                        (Properties.ProbDiffFac / neighborsCount) * probabilitySum;

        float newProbability = (1f - Properties.ProbDiffFac) * sS.GetProbability() +
                               probabilitySum / neighborsCount;


        sS.SetProb(newProbability);
    }

    // Check for the seen search segments
    public void CheckSeenSs(List<Guard> guards, RoadMapLine line)
    {
        foreach (var guard in guards)
        {
            // Trim the parts seen by the guards and reset the section if it is all seen 
            line.CheckSeenSegment(guard);
        }
    }


    // Normalize the probabilities of the segments
    // if the max prob is zero, then find the max prob
    public void NormalizeProbs(float maxProb)
    {
        foreach (var line in m_roadMap.GetLines())
        {
            SearchSegment sS = line.GetSearchSegment();

            if (maxProb == 0f)
            {
                sS.SetProb(1f);
                continue;
            }

            sS.SetProb(sS.GetProbability() / maxProb);
        }
    }


    // Get the best Search segment the guard should visit.
    public Vector2? GetSearchSegment(Guard requestingGuard, List<Guard> guards, Intruder intruder,
        List<MeshPolygon> navMesh, SearchWeights searchWeights)
    {
        SearchSegment bestSs = null;
        float maxFitnessValue = Mathf.NegativeInfinity;
        float maxProbability = Mathf.NegativeInfinity;

        // Loop through the search segments in the lines
        foreach (var line in m_roadMap.GetLines())
        {
            SearchSegment sS = line.GetSearchSegment();

            if (maxProbability < sS.GetProbability())
            {
                maxProbability = sS.GetProbability();
            }

            // Skip the segment if it has a probability of zero or less
            if (sS.GetProbability() <= m_minSegThreshold)
                continue;


            // Get the distance of the closest goal other guards are coming to visit
            float minGoalDistance = Mathf.Infinity;

            foreach (var guard in guards)
            {
                // Skip the busy guards
                if (!guard.IsBusy())
                    continue;

                float distanceToGuardGoal =
                    PathFinding.GetShortestPathDistance(navMesh, sS.GetMidPoint(), guard.GetGoal().Value);

                if (minGoalDistance > distanceToGuardGoal)
                {
                    minGoalDistance = distanceToGuardGoal;
                }
            }

            minGoalDistance = minGoalDistance == Mathf.Infinity ? 0f : minGoalDistance;

            // Get the distance from the requesting guard
            float distanceToGuard = PathFinding.GetShortestPathDistance(navMesh, (sS.position1 + sS.position2) / 2f,
                requestingGuard.transform.position);

            // Calculate the fitness of the search segment
            // start with the probability
            float ssFitness = sS.GetFitness();

            // Calculate the overall heuristic of this search segment
            ssFitness = ssFitness * searchWeights.probWeight +
                        (sS.GetAge() / Properties.MaxAge) * searchWeights.ageWeight +
                        (minGoalDistance / Properties.MaxPathDistance) * searchWeights.dstToGuardsWeight +
                        (distanceToGuard / Properties.MaxPathDistance) * searchWeights.dstFromOwnWeight;


            if (maxFitnessValue < ssFitness)
            {
                maxFitnessValue = ssFitness;
                bestSs = sS;
            }
        }

        if (bestSs == null)
            return null; //intruder.GetLastKnownLocation();

        return (bestSs.position1 + bestSs.position2) / 2f;
    }


    // // Get a complete path of no more than param@length that a guard needs to traverse to search for an intruder.
    public void GetPath(Guard guard)
    {
        m_roadMap.GetPath(guard);
    }


    // The search is over so clear the variables
    public void Clear()
    {
        m_roadMap.ClearSearchSegments();
    }

    public void OnDrawGizmos()
    {
        if (RenderSearchSegments)
            if (m_roadMap != null)
            {
                m_roadMap.DrawSearchSegments();
            }
    }
}