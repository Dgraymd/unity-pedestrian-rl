﻿using MLAgents;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.Characters.ThirdPerson;
using UnityStandardAssets.CrossPlatformInput;

public enum Direction { FREE, UP, DOWN, LEFT, RIGHT }

/// <summary>
/// This class is for reinforcement learning on a AI Third Person Controller prefab
/// This requires an agent route for navigation, and
/// an current target Game Object for controlling the direction.
/// Be sure to place the agent on a NavMesh area.
/// </summary>
public class PedestrianRoutePlanRL : Agent
{
    public PedestrianEnd endNode;
    public GameObject[] obstacles; // List of all obstacles
    private GameObject[] predictedPOC; //Predicted possible point of conflict
    public GameObject POCPrefab;

    private AgentRoute agentRoute;
    private int noOfRouteNodes;

    private Vector3 envCentre;
    private Vector3 startingPosition;
    private float envScale = 1.0f;

    private const float RESET_BOUNDARY_LIMIT = 25f;
    private const float FOLLOW_MULTIPLIER = 1f;
    const float ENV_LENGTH = 22f;
    const int MAX_ENV_STEPS = 300;

    private int resetCounter;
    private bool resetEnv; // Retrain the episode without resetting
    private bool _justCalledDone; 

    // Curriculum settings
    Academy routeplanAcademy;
    int noOfObstacles;
    int randomisedObsPos;

    const bool IS_TRAINING = true; 

    void OnDrawGizmosSelected()
    {
        // Draw a the route path when selected in Unity
        agentRoute = this.GetComponent<PedestrianDecisionControl>().agentRoute;
        // Draw the line from agent to direction vector object if exists
        if (this.GetComponent<PedestrianDecisionControl>().direction != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(this.transform.position, this.GetComponent<PedestrianDecisionControl>().direction.transform.position);
        }

        // Draw first line from this transform to the target
        Gizmos.color = Color.white;
        Gizmos.DrawLine(this.transform.position, agentRoute.activeRoute[0].transform.position);

        // Draw the path
        for (int i = 0; i < agentRoute.activeRoute.Length - 1; i++)
            Gizmos.DrawLine(agentRoute.activeRoute[i].transform.position, agentRoute.activeRoute[i + 1].transform.position);
    }

    // Start is called before the first frame update
    void Start()
    {
        // Get centre position of the environment
        envCentre = this.transform.parent.transform.position;

        routeplanAcademy = FindObjectOfType<AcademyPedestrian>();
        noOfObstacles = (int)routeplanAcademy.resetParameters["noOfObstacles"];
        randomisedObsPos = (int)routeplanAcademy.resetParameters["randomisedObsPos"];

        // Get agentRoute from AICharacterBehaviour script
        agentRoute = this.GetComponent<PedestrianDecisionControl>().agentRoute;
        noOfRouteNodes = agentRoute.activeRoute.Length - 2; // Exclude endNode & startNode
        startingPosition = this.transform.localPosition;

        resetEnv = true;
        _justCalledDone = false;

        //for (int i = 0; i < obstacles.Length; i++)
        //{
        //    obstacles[i].GetComponent<Obstacle>().isActive = false;

        //    foreach (Transform child in obstacles[i].transform)
        //        child.GetComponent<Renderer>().enabled = false;
        //}

        predictedPOC = new GameObject[obstacles.Length];
        for (int i = 0; i < obstacles.Length; i++)
        {
            if (!POCPrefab)
                predictedPOC[i] = new GameObject("Predicted POC" + (i + 1));
            else
                predictedPOC[i] = Instantiate(POCPrefab);

            predictedPOC[i].AddComponent<Obstacle>();
            predictedPOC[i].transform.parent = this.transform;
        }

        AgentReset();
    }

    public override void CollectObservations()
    {
        AddVectorObs((this.transform.position.x - envCentre.x + 5) / 10f);
        AddVectorObs((endNode.transform.position.x - envCentre.x + 5) / 10f);
        if (predictedPOC[0].GetComponent<Obstacle>().isActive)
        {
            AddVectorObs(true);

            AddVectorObs((predictedPOC[0].transform.position.x - envCentre.x + 5) / 10f);
            AddVectorObs((predictedPOC[0].transform.position.z - envCentre.z + 5) / 10f);

            AddVectorObs(predictedPOC[0].GetComponent<Obstacle>().size);
            AddVectorObs(predictedPOC[0].GetComponent<Obstacle>().dangerLevel);
        }
        else
        {
            AddVectorObs(false);
            AddVectorObs(-1f);
            AddVectorObs(-1f);

            AddVectorObs(0f); AddVectorObs(0f);
        }
        AddVectorObs(envScale);
    }

    public override void AgentReset()
    {
        resetCounter++;
        //! TRAINING RESET
        /// Description: Randomise the number of obstacles and various obstacle properties. Only
        /// static obstacle is used in the training step. 
        if (IS_TRAINING)
        {
            if (resetEnv || resetCounter > MAX_ENV_STEPS)
            {
                if (resetCounter > MAX_ENV_STEPS) resetCounter = 0;

                // Get number of obstacles for curriculum learning
                noOfObstacles = (int)routeplanAcademy.resetParameters["noOfObstacles"];
                randomisedObsPos = (int)routeplanAcademy.resetParameters["randomisedObsPos"];

                // Randomise environment scale
                envScale = Random.Range(0.2f, 1f);

                // Reset to original position
                this.transform.localPosition = new Vector3(Random.Range(-5f, 5f), 0, zScaled(-12f));
                this.endNode.transform.localPosition = new Vector3(Random.Range(-5f, 5f), 0.5f, zScaled(10f));

                // Randomise obstacle 
                for (int i = 0; i < noOfObstacles; i++)
                {
                    //// Randomise enability of obstacles
                    if (randomisedObsPos == 0)
                        obstacles[i].GetComponent<Obstacle>().isActive = true;
                    else
                        obstacles[i].GetComponent<Obstacle>().isActive = Random.value > 0.5f;

                    if (obstacles[i].GetComponent<Obstacle>().isActive)
                    {
                        // Randomise position
                        obstacles[i].transform.localPosition = new Vector3(Random.Range(-5f, 5f), 0, Random.Range(zScaled(-10f), zScaled(8f)));
                        // Randomise size
                        obstacles[i].GetComponent<Obstacle>().size = Random.Range(0.5f, 2f);
                        // Randomise danger level
                        obstacles[i].GetComponent<Obstacle>().dangerLevel = Random.Range(0f, 1f);
                    }
                }
            }

        }
        /// In testing, various attributes can be manually set. Also obstacle type may also includes
        /// moving obstacles such as pedestrian obstacle or simple movement obstacle.
        else
        {
            envScale = 0.4f;

            noOfObstacles = 1;
            randomisedObsPos = 1;
            this.transform.localPosition = new Vector3(4f, 0, zScaled(-12f));
            this.endNode.transform.localPosition = new Vector3(2f, 0.5f, zScaled(10f));

            for (int i = 0; i < noOfObstacles; i++)
            {
                //obstacles[i].GetComponent<Obstacle>().isActive = Random.value > 0.5f;
                obstacles[i].GetComponent<Obstacle>().isActive = true;
                if (obstacles[i].GetComponent<Obstacle>().isActive)
                {
                    obstacles[i].GetComponent<Obstacle>().size = 0.8f;
                    obstacles[i].GetComponent<Obstacle>().dangerLevel = 0.9f;
                    //obstacles[i].transform.localPosition = new Vector3(Random.Range(-5f, 5f), 0, Random.Range(zScaled(-10f), zScaled(8f)));
                    if (obstacles[i].GetComponent<PedestrianObstacle>()) // Obstacle type is pedestrian obstacle
                    {
                        Vector3 obstaclePosition = new Vector3(-4.5f, 0, zScaled(8.5f));
                        // Only set the z position to obstacle. The x position is set to the obstacle agent instead
                        obstacles[i].transform.localPosition = new Vector3(0, 0, obstaclePosition.z);
                        obstacles[i].GetComponent<PedestrianObstacle>().pedestrianObstacleAgent.transform.localPosition
                            = new Vector3(obstaclePosition.x, 0, 0);
                    }
                }

            }
        }

        for (int i = 0; i < noOfObstacles; i++)
        {
            // Get predicted point of collision
            SetPredictedPOC(predictedPOC[i], obstacles[i]);

            //Display or hide obstacle(s) on scene
            if (obstacles[i].GetComponent<PedestrianObstacle>()) { }
            else if (obstacles[i].GetComponent<Obstacle>())
            {
                if (obstacles[i].GetComponent<Obstacle>().isActive)
                {
                    // Scale obstacle's size
                    float obstacleSize = obstacles[i].GetComponent<Obstacle>().size;
                    obstacles[i].transform.localScale = new Vector3(obstacleSize, obstacleSize, obstacleSize);

                    // Displayed on scene
                    foreach (Transform child in obstacles[i].transform)
                        if (child.GetComponent<Renderer>())
                            child.GetComponent<Renderer>().enabled = true;
                }
                else
                {
                    // Hidden on scene
                    foreach (Transform child in obstacles[i].transform)
                        if (child.GetComponent<Renderer>())
                            child.GetComponent<Renderer>().enabled = false;
                }
            }

        }

        startingPosition = this.transform.localPosition;
        agentRoute.activeRoute[0].gameObject.transform.localPosition = startingPosition;
    }

    public override void AgentAction(float[] vectorAction, string textAction)
    {
        if (_justCalledDone)
        {
            _justCalledDone = false;
        }
        else
        {
            resetEnv = true;

            // Set position for each route node
            for (int i = 0; i < vectorAction.Length; i++)
            {
                Vector3 nodePos = new Vector3(vectorAction[i] * 5, 0.5f, zScaled(i*2 - 10f));
                agentRoute.activeRoute[i + 1].gameObject.transform.localPosition = nodePos;
            }

            if (IS_TRAINING)
            {
                // REWARDS
                // Shortest route possible
                AddReward(SumSqrPathLength() * -0.03f * envScale);

                // Least direction changes possible 
                for (int i = 0; i < noOfRouteNodes; i++)
                {
                    Vector2 prevRouteAngle, nextRouteAngle;
                    prevRouteAngle = new Vector2(RouteNode(i + 1).x - RouteNode(i).x,
                                                 RouteNode(i + 1).z - RouteNode(i).z);
                    nextRouteAngle = new Vector2(RouteNode(i + 2).x - RouteNode(i + 1).x,
                                                 RouteNode(i + 2).z - RouteNode(i + 1).z);
                    // Only put a penalty to the agent if the change in direction is > 30*
                    if (Mathf.Abs(Vector2.Angle(prevRouteAngle, nextRouteAngle)) > 30f)
                    {
                        AddReward(-0.03f);
                    }
                }
                // Calculate the position on the road in order to apply various rewards
                for (int i = 0; i < noOfRouteNodes + 1; i++)
                {
                    int NUMBER_OF_SAMPLES = 20; // The higher the number, the more positions are added to rewards
                    float xInterpolant = 1f / (float)NUMBER_OF_SAMPLES;

                    // Favour walking parallel to the boundary
                    if (Mathf.Abs(RouteNode(i + 1).x - RouteNode(i).x) > 0.4f)
                        AddReward(-0.01f * envScale);

                    for (int j = 0; j < NUMBER_OF_SAMPLES; j++)
                    {
                        Vector2 prevNode = new Vector2(RouteNode(i).x, RouteNode(i).z);
                        Vector2 currentNode = new Vector2(RouteNode(i + 1).x, RouteNode(i + 1).z);
                        // Get current x position of the route for each iteration
                        Vector2 simulatedPos = Vector2.Lerp(prevNode, currentNode, j * xInterpolant);

                        AddReward(XPosReward(simulatedPos.x));
                        AddReward(ObstacleReward(simulatedPos));
                    }
                }
            
            }
            Done();
            _justCalledDone = true;

        }
    }


    private float SumSqrPathLength()
    {
        float sqrPathLength = 0;

        for (int i = 0; i < noOfRouteNodes + 1; i++)
        {
            Vector2 pathDist; // Distance between two nodes
            pathDist = new Vector2(RouteNode(i + 1).x - RouteNode(i).x,
                                   RouteNode(i + 1).z - RouteNode(i).z);
            sqrPathLength += Vector2.SqrMagnitude(pathDist);
        }

        return sqrPathLength;
    }

    // Add reward based on current x position of the agent
    private float XPosReward(float xPosition)
    {
        float totalReward = 0f;
        // Favor walking on the left
        if (xPosition < 0)
            totalReward += 2f * envScale;
        else
            totalReward -= 2f * envScale;

        // Avoid being too close to the boundary
        if (xPosition > 4.5f || xPosition < -4.5f)
            totalReward -= 1f;

        return totalReward * 0.001f;
    }

    // Add reward based on distance to obstacle(s)
    private float ObstacleReward(Vector2 currentPosition)
    {
        float totalReward = 0f;

        for (int i = 0; i < obstacles.Length; i++)
        {
            if (obstacles[i].GetComponent<Obstacle>().isActive)
            {
                Vector2 obstaclePos = new Vector2(obstacles[i].transform.localPosition.x, obstacles[i].transform.localPosition.z);
                float distCoef = Vector2.SqrMagnitude(obstaclePos - currentPosition) - obstacles[i].GetComponent<Obstacle>().size * obstacles[i].GetComponent<Obstacle>().size;
                if (distCoef < 0) // Route conflict
                {
                    resetEnv = false;
                    totalReward += distCoef / (obstacles[i].GetComponent<Obstacle>().size * obstacles[i].GetComponent<Obstacle>().size) * 
                                               Mathf.Pow(obstacles[i].GetComponent<Obstacle>().dangerLevel, 2);
                    Done();
                }
                else
                {
                    // Add a small bonus if there're obstacle(s) and you're not touching them
                    totalReward += 0.01f * Mathf.Pow(obstacles[i].GetComponent<Obstacle>().dangerLevel, 2);
                }
            }
        }

        return totalReward * 0.1f;
    }

    private void SetPredictedPOC(GameObject POC, GameObject obstacle)
    {
        POC.GetComponent<Obstacle>().Clone(obstacle);
        if (POCPrefab != null)
        {
            if (obstacle.GetComponent<Obstacle>().isActive)
                POC.GetComponent<Renderer>().enabled = true;
            else
                POC.GetComponent<Renderer>().enabled = false;
        }

        if (obstacle.GetComponent<SimpleMovingObstacle>())
        {
            float distanceToObs = obstacle.transform.position.z - this.transform.position.z;
            float obsVelocity = obstacle.GetComponent<SimpleMovingObstacle>().obstacleSpeed;
            float agentVelocity = this.GetComponent<PedestrianDecisionControl>().currentSpeed;

            Direction obsDirection = obstacle.GetComponent<SimpleMovingObstacle>().obstacleDirection;

            if (obsDirection == Direction.FREE)
            {
                // Get the obstacle's direction
                Transform obsDirectionTransform = obstacle.GetComponent<SimpleMovingObstacle>().direction;
                if (!obsDirectionTransform) Debug.LogError("A direction transform is required for free direction movement.");
                Vector3 obsDirectionVector = (obsDirectionTransform.position - obstacle.transform.position).normalized;
                
                // Calculate predicted time until impact
                // Original equation
                //     (obs.z - agent.z) * obs.spd
                // t =  --------------------------  if agent.spd + obs.spd*cos θ > 0
                //      agent.spd + obs.spd*cos θ
                // Calculate cos θ
                float cos_theta = Mathf.Cos(Vector3.Angle(obsDirectionVector, new Vector3(0,0,-1)) * Mathf.Deg2Rad);
                float t_denominator = agentVelocity + obsVelocity * cos_theta;
                if (t_denominator <= 0)
                    POC.GetComponent<Obstacle>().isActive = false;
                else
                {
                    float t = distanceToObs * obsVelocity / t_denominator;

                    POC.transform.position = obstacle.transform.position + t * obsDirectionVector;
                    Vector3 obsToEnv = POC.transform.position - envCentre;
                    if (obsToEnv.z > zScaled(10f) || obsToEnv.z < zScaled(-12f) || obsToEnv.x < -5f || obsToEnv.x > 5f) 
                        POC.GetComponent<Obstacle>().isActive = false;
                }
            }
            else if (obsDirection == Direction.DOWN)
            {
                float obsToPOC = distanceToObs * obsVelocity / (agentVelocity + obsVelocity);
                POC.transform.position = new Vector3(obstacle.transform.position.x, obstacle.transform.position.y, obstacle.transform.position.z - obsToPOC);
            }
            else if (obsDirection == Direction.UP)
            {
                if (obsVelocity >= agentVelocity) POC.GetComponent<Obstacle>().isActive = false; 
                else
                {
                    float obsToPOC = distanceToObs * obsVelocity / (agentVelocity - obsVelocity);
                    if (distanceToObs + obsToPOC > ENV_LENGTH) POC.GetComponent<Obstacle>().isActive = false;
                    else POC.transform.position = new Vector3(obstacle.transform.position.x, obstacle.transform.position.y, obstacle.transform.position.z + obsToPOC);
                }
            }
            else if (obsDirection == Direction.RIGHT)
            {
                float obsToPOC = distanceToObs * obsVelocity / agentVelocity;
                if (obstacle.transform.localPosition.x + obsToPOC > 5f) obsToPOC = 5f - obstacle.transform.localPosition.x;
                POC.transform.position = new Vector3(obstacle.transform.position.x + obsToPOC, obstacle.transform.position.y, obstacle.transform.position.z);
            }
            else if (obsDirection == Direction.LEFT)
            {
                float obsToPOC = distanceToObs * obsVelocity / agentVelocity;
                if (obstacle.transform.localPosition.x - obsToPOC < -5f) obsToPOC = 5f + obstacle.transform.localPosition.x;
                POC.transform.position = new Vector3(obstacle.transform.position.x - obsToPOC, obstacle.transform.position.y, obstacle.transform.position.z);
            }
        }
        else if (obstacle.GetComponent<PedestrianObstacle>())
        {
            float distanceToObs = obstacle.transform.position.z - this.transform.position.z;
            float obsVelocity = obstacle.GetComponent<PedestrianObstacle>().pedestrianObstacleAgent.GetComponent<PedestrianDecisionControl>().currentSpeed;
            float agentVelocity = this.GetComponent<PedestrianDecisionControl>().currentSpeed;
            PedestrianDest[] predictedRoute = obstacle.GetComponent<PedestrianObstacle>().GetPredictedRoute().activeRoute;

            // Calculate POC's x position in agent's path
            float obsToPOC = distanceToObs * obsVelocity / (agentVelocity + obsVelocity);

            int i = (int)(obsToPOC * 10 / 22);
            float fraction = (obsToPOC - i * 2) / 2f;
            Vector3 POCposition = Vector3.Lerp(predictedRoute[i].transform.position, predictedRoute[i + 1].transform.position, fraction);
            POC.transform.position = POCposition;
        }
        else if (obstacle.GetComponent<Obstacle>())
        {
            POC.transform.position = obstacle.transform.position;
        }
    }


    /// <summary>
    /// This function returns the scaled value of z position in case of a scaled environment
    /// </summary>
    /// <param name="zPos"></param>
    /// <returns></returns>
    private float zScaled(float zPos)
    {
        return envScale * zPos;
    }

    private Vector3 RouteNode(int i) { return agentRoute.activeRoute[i].gameObject.transform.localPosition; }

}
