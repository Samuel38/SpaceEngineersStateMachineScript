﻿using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        class MyOptions : Options
        {
            public override Dictionary<string, object> getValues()
            {
                return new Dictionary<string, object>();
            }

            public override void parseArg(string arg)
            {

            }

            public override void setDefaults()
            {

            }
        }

        enum CruiseDebug
        {
            None,
            Forward,
            Horizontal,
            Vertical,
            Pitch,
            Roll,
            CustomTest
        }

        class NearestPlanet
        {
            private static readonly Vector3[] POSITIONS =
            {
                new Vector3(0, 0, 0),
                new Vector3(16384, 136384, -113616),
                new Vector3(1031072, 131072, 1631072),
                new Vector3(916384, 16384, 1616384),
                new Vector3(131072, 131072, 5731072),
                new Vector3(36384, 226384, 5796384),
            };

            private static readonly string[] NAMES =
            {
                "Earth",
                "Moon",
                "Mars",
                "Europa",
                "Alien",
                "Titan"
                    // todo missing triton & moon
            };

            private static readonly double[] RADII =
            {
                60000,
                9500,
                60000,
                9500,
                60000,
                9500
            };

            private static readonly double[] ORBIT_RADII =
            {
                103097,
                12315,
                101557,
                12674,
                104510,
                12315
            };

            private static readonly TimeSpan UPDATE_PERIOD = new TimeSpan(0, 5, 0);

            private bool _initialised = false;
            private TimeSpan _timeSinceLastUpdate = new TimeSpan();
            private int _nearestPlanet = 0;

            public void reset()
            {
                _initialised = false;
                _timeSinceLastUpdate = new TimeSpan();
                _nearestPlanet = 0;
            }

            public void update(Vector3 gridPosition, TimeSpan timeSinceLast, bool force = false)
            {
                _timeSinceLastUpdate += timeSinceLast;
                if (!_initialised || force || _timeSinceLastUpdate >= UPDATE_PERIOD)
                {
                    _nearestPlanet = findNearest(gridPosition);
                    _timeSinceLastUpdate = new TimeSpan();
                    _initialised = true;
                }
            }

            public Vector3 getNearestPlanetPosition()
            {
                return POSITIONS[_nearestPlanet];
            }

            public string getNearestPlanetName()
            {
                return NAMES[_nearestPlanet];
            }

            public double getNearestPlanetRadius()
            {
                return RADII[_nearestPlanet];
            }

            public double getNearestPlanetOrbitRadius()
            {
                return ORBIT_RADII[_nearestPlanet];
            }

            private static int findNearest(Vector3 gridPosition)
            {
                double minDist = double.PositiveInfinity;
                int minDistIndex = -1;
                for (int i = 0; i < POSITIONS.Length; ++i)
                {
                    double dist = (gridPosition - POSITIONS[i]).Length();
                    if (dist < minDist)
                    {
                        minDist = dist;
                        minDistIndex = i;
                    }
                }
                return minDistIndex;
            }
        }


        class MyContext : Context<MyContext>
        {
            public static readonly StoppedState Stopped = new StoppedState();

            public Thrust _thrust = new Thrust();
            public List<IMyGyro> _gyros = new List<IMyGyro>(16);
            public List<Gyroscope> _compensatedGyros = new List<Gyroscope>(16);
            public List<IMyTextPanel> _textPanels = new List<IMyTextPanel>(1);
            public List<IMyTextPanel> _textPanelsAux = new List<IMyTextPanel>(1);
            public List<IMyTextPanel> _textPanelsDebug = new List<IMyTextPanel>(1);
            public List<IMyShipController> _cockpits = new List<IMyShipController>(16);
            public List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>(16);
            public List<IMyTerminalBlock> _targetingBlock = new List<IMyTerminalBlock>(1);
            public CruiseDebug _debug = CruiseDebug.None;
            public VelocityTracker _velocityTracker = new VelocityTracker();
            public Vector3D _planetPosition = new Vector3D();
            public Vector3D _directionFromPlanetToMe = new Vector3D();
            private StringBuilder _stringBuilder = new StringBuilder();
            public NearestPlanet _nearestPlanet = new NearestPlanet();
            public double _lastPitchError_rads = 0;
            public double _lastRollError_rads = 0;
            private Vector3D _targetPosition = new Vector3D();
            private double _gridMass = 0.0;

            public static readonly float ORBIT_SAFETY_MARGIN = 200;

            public MyContext(StateMachineProgram<MyContext> program) : base(program, new MyOptions(), Stopped)
            {

            }

            private void updatePlanetInfo(TimeSpan timeSinceLastUpdate)
            {
                Vector3D gridPosition = getTargetingBlockPosition();
                _nearestPlanet.update(gridPosition, timeSinceLastUpdate);
                _planetPosition = _nearestPlanet.getNearestPlanetPosition();

                _directionFromPlanetToMe = (gridPosition - _planetPosition);
                _directionFromPlanetToMe.Normalize();
            }

            public override void update(TimeSpan timeSinceLastUpdate)
            {
                _velocityTracker.update(getTargetingBlockPosition(), timeSinceLastUpdate);
                updatePlanetInfo(timeSinceLastUpdate);

                base.update(timeSinceLastUpdate);
            }

            protected override bool updateBlocksImpl()
            {
                log("Update blocks");

                updateTargetPositionFromCustomData();
                
                _textPanels.Clear();
                Program.GridTerminalSystem.GetBlocksOfType(_textPanels, b => b.CustomName.Contains("CruiseMain"));

                _textPanelsAux.Clear();
                Program.GridTerminalSystem.GetBlocksOfType(_textPanelsAux, b => b.CustomName.Contains("CruiseAux"));

                _textPanelsDebug.Clear();
                Program.GridTerminalSystem.GetBlocksOfType(_textPanelsDebug, b => b.CustomName.Contains("CruiseDebug"));

                _cockpits.Clear();
                // Todo make cockpit name an argument
                Program.GridTerminalSystem.GetBlocksOfType(_cockpits, b => b.CustomName.Contains("CruiseControl"));

                _targetingBlock.Clear();
                Program.GridTerminalSystem.GetBlocksOfType(_targetingBlock, b => b.CustomName.Contains("CruiseTargeting"));

                _batteries.Clear();
                Program.GridTerminalSystem.GetBlocksOfType(_batteries);

                _gyros.Clear();
                Program.GridTerminalSystem.GetBlocksOfType(_gyros);

                if (_cockpits.Count > 0)
                {
                    _compensatedGyros.Clear();
                    foreach (IMyGyro gyro in _gyros)
                    {
                        _compensatedGyros.Add(new Gyroscope(gyro, _cockpits[0]));
                    }

                    _thrust.update(Program.GridTerminalSystem, _cockpits[0]);

                    _gridMass = _cockpits[0].CalculateShipMass().PhysicalMass;
                }

                // At least one cockpit and targeting block are required
                return _cockpits.Count > 0 && _targetingBlock.Count > 0;
            }

            protected override void updateDisplayImpl()
            {
                _stringBuilder.Clear();
                _stringBuilder.Append("Orbital Targeting\n");
                if (!FoundAllBlocks)
                {
                    _stringBuilder.Append("ERROR: Missing blocks; ensure cockpit and targeting block setup\n");
                }

                bool isCruising = State is CruiseControlState;
                _stringBuilder.Append("Status:\n  ");
                if (!FoundAllBlocks)
                {
                    _stringBuilder.Append("Unavailable");
                }
                else
                {
                    _stringBuilder.Append(isCruising ? "Cruising" : "Off");
                }
                _stringBuilder.Append("\n");

                _stringBuilder.Append("Nearest planet:\n  ");
                _stringBuilder.Append(string.Format("{0}", _nearestPlanet.getNearestPlanetName()));
                _stringBuilder.Append("\n");

                double distanceToPlanetSurface = 
                    (getTargetingBlockPosition() - _planetPosition).Length() 
                    - _nearestPlanet.getNearestPlanetRadius();
                _stringBuilder.Append(string.Format("Distance to {0}:\n  ", _nearestPlanet.getNearestPlanetName()));
                _stringBuilder.Append(string.Format("{0:0.00} km", distanceToPlanetSurface / 1000));
                _stringBuilder.Append("\n");

                _stringBuilder.Append(string.Format("Error:\n  pitch={0:0.00} deg\n  roll={1:0.00} deg", _lastPitchError_rads * 180 / Math.PI, _lastRollError_rads * 180 / Math.PI));
                _stringBuilder.Append("\n");

                double errorX = distanceToPlanetSurface * Math.Sin(_lastPitchError_rads);
                double errorY = distanceToPlanetSurface * Math.Sin(_lastRollError_rads);
                double errorAbs = Math.Sqrt(errorX * errorX + errorY * errorY);
                _stringBuilder.Append(string.Format("Accuracy:\n  +/- {0:0.00} m", errorAbs));

                string text = _stringBuilder.ToString();

                foreach (IMyTextPanel panel in _textPanels)
                {
                    panel.ContentType = ContentType.TEXT_AND_IMAGE;
                    panel.WriteText(text);
                }




                _stringBuilder.Clear();




                Vector3D firingPosition = getFiringPosition();
                Vector3D position = getTargetingBlockPosition();
                Vector3D positionError = position - firingPosition;
                _stringBuilder.Append("Fire pos:\n");
                _stringBuilder.Append(string.Format("  X={0:0.0000}\n  Y={1:0.0000}\n  Z={2:0.0000}\n", firingPosition.X, firingPosition.Y, firingPosition.Z));

                _stringBuilder.Append("Dist to fire pos:\n");
                _stringBuilder.Append(string.Format("  X={0:0.0000}\n  Y={1:0.0000}\n  Z={2:0.0000}\n", positionError.X, positionError.Y, positionError.Z));

                text = _stringBuilder.ToString();

                foreach (IMyTextPanel panel in _textPanelsAux)
                {
                    panel.ContentType = ContentType.TEXT_AND_IMAGE;
                    panel.WriteText(text);
                }
            }

            public Vector3D getFiringPosition()
            {
                Vector3D directionVector = _targetPosition - _nearestPlanet.getNearestPlanetPosition();
                directionVector.Normalize();
                return directionVector * (_nearestPlanet.getNearestPlanetOrbitRadius() + MyContext.ORBIT_SAFETY_MARGIN);
            }

            public Vector3D getTargetingBlockPosition()
            {
                if (_targetingBlock.Count > 0)
                {
                    return _targetingBlock[0].GetPosition();
                }
                else
                {
                    return Program.Me.CubeGrid.GetPosition();
                }
            }

            public void updateTargetPositionFromCustomData()
            {
                string data = Program.Me.CustomData.Trim();
                if (data.Length == 0)
                {
                    _targetPosition = new Vector3();
                    return;
                }

                string[] parts = data.Split(' ');
                _targetPosition = new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
            }

            public void displayDebugText(string text, bool append = false)
            {
                if (_debug == CruiseDebug.None)
                {
                    return;
                }

                foreach (IMyTextPanel panel in _textPanelsDebug)
                {
                    panel.ContentType = ContentType.TEXT_AND_IMAGE;
                    panel.WriteText(text, append);
                }
            }

            public Vector3D getAcceleration()
            {
                Vector3D directionForward = _cockpits[0].WorldMatrix.GetDirectionVector(Base6Directions.Direction.Forward);
                Vector3D directionUp = _cockpits[0].WorldMatrix.GetDirectionVector(Base6Directions.Direction.Up);
                Vector3D directionRight = _cockpits[0].WorldMatrix.GetDirectionVector(Base6Directions.Direction.Right);

                double m = _gridMass;
                if (m == 0)
                {
                    return new Vector3D();
                }
                return (_thrust.currentThrust_Newtons(Thrust.FWD) - _thrust.currentThrust_Newtons(Thrust.REV)) / m * directionForward
                    + (_thrust.currentThrust_Newtons(Thrust.UP) - _thrust.currentThrust_Newtons(Thrust.DOWN)) / m * directionUp
                    + (_thrust.currentThrust_Newtons(Thrust.RIGHT) - _thrust.currentThrust_Newtons(Thrust.LEFT)) / m * directionRight;
            }
        }

        class StoppedState : State<MyContext>
        {
            public override void update(MyContext context)
            {
                context.log("Stopped");
            }
        }

        private static double getBatteryOutput_MW(List<IMyBatteryBlock> batteries)
        {
            double total = 0;
            foreach (IMyBatteryBlock block in batteries)
            {
                total += block.CurrentOutput;
            }
            return total;
        }

        class VelocityTracker
        {
            bool _velocityInitialised = false;
            private Vector3D _vMeasLastPosition = new Vector3D();
            public Vector3D Velocity { get; private set; }

            public bool IsInitialized { get { return _velocityInitialised; } }

            public VelocityTracker()
            {
                Velocity = new Vector3D();
            }

            public void update(Vector3D position, TimeSpan timeSinceLast)
            {
                double seconds = timeSinceLast.TotalSeconds;
                if (_velocityInitialised && seconds > 0)
                {
                    Velocity = (position - _vMeasLastPosition) / seconds;
                }
                else
                {
                    Velocity = new Vector3D();
                }
                _velocityInitialised = true;
                _vMeasLastPosition = position;
            }
        }

        //Hellothere_1's Fast Gyroscope Adjustment Code

        public class Gyroscope
        {
            public IMyGyro gyro;
            private int[] conversionVector = new int[3];

            public Gyroscope(IMyGyro gyroscope, IMyTerminalBlock reference)
            {
                gyro = gyroscope;

                for (int i = 0; i < 3; i++)
                {
                    Vector3D vectorShip = GetAxis(i, reference);

                    for (int j = 0; j < 3; j++)
                    {
                        double dot = vectorShip.Dot(GetAxis(j, gyro));

                        if (dot > 0.9)
                        {
                            conversionVector[j] = i;
                            break;
                        }
                        if (dot < -0.9)
                        {
                            conversionVector[j] = i + 3;
                            break;
                        }
                    }
                }
            }

            public void SetRotation(float[] rotationVector, float gyroPower)
            {
                gyro.GyroOverride = true;
                gyro.GyroPower = gyroPower;
                gyro.Pitch = rotationVector[conversionVector[0]];
                gyro.Yaw = rotationVector[conversionVector[1]];
                gyro.Roll = rotationVector[conversionVector[2]];
            }

            private Vector3D GetAxis(int dimension, IMyTerminalBlock block)
            {
                switch (dimension)
                {
                    case 0:
                        return block.WorldMatrix.Right;
                    case 1:
                        return block.WorldMatrix.Up;
                    default:
                        return block.WorldMatrix.Backward;
                }
            }
        }

        class CruiseControlState : State<MyContext>
        {
            private static double IMIN = -0.25;
            private static double IMAX = 0.25;

            //private static double P_FWD = 10;
            //// We can't use integral control on this one because it's unable to 
            //// overshoot, meaning the integrator will just get stuck at the max
            //private static double I_FWD = 0.00;
            //private static double D_FWD = 0.2;
            private static double P_FWD = 10;
            // We can't use integral control on this one because it's unable to 
            // overshoot, meaning the integrator will just get stuck at the max
            private static double I_FWD = 0.0;
            private static double D_FWD = -1;
            //private static double D_FWD = 0;

            private static double P_VERT = P_FWD;
            private static double I_VERT = I_FWD;
            private static double D_VERT = D_FWD;

            private static double P_HORZ = P_FWD;
            private static double I_HORZ = I_FWD;
            private static double D_HORZ = D_FWD;

            //private static double P_GYRO = 4;
            //private static double I_GYRO = 0.01;
            //private static double D_GYRO = 0.02;
            //private static double IMIN_GYRO = -1;
            //private static double IMAX_GYRO = 1;

            private static double P_GYRO = 4;
            private static double I_GYRO = 0.01;
            private static double D_GYRO = -2;
            private static double IMIN_GYRO = -1;
            private static double IMAX_GYRO = 1;

            private PidController _fwdController = new PidController(P_FWD, I_FWD, D_FWD, IMIN, IMAX);
            private PidController _vertController = new PidController(P_VERT, I_VERT, D_VERT, IMIN, IMAX);
            private PidController _horzController = new PidController(P_HORZ, I_HORZ, D_HORZ, IMIN, IMAX);
            private PidController _pitchController = new PidController(P_GYRO, I_GYRO, D_GYRO, IMIN_GYRO, IMAX_GYRO);
            private PidController _rollController = new PidController(P_GYRO, I_GYRO, D_GYRO, IMIN_GYRO, IMAX_GYRO);

            private float[] _rotationVector = new float[6];

            private Vector3D _lastPosition = new Vector3D();

            private TimeSpan _timeRunning = new TimeSpan();

            public override void update(MyContext context)
            {
                if (!context.FoundAllBlocks)
                {
                    context.transition(MyContext.Stopped);
                }

                TimeSpan timeSinceLast = context.Program.Runtime.TimeSinceLastRun;

                Vector3D directionForward = context._cockpits[0].WorldMatrix.GetDirectionVector(Base6Directions.Direction.Forward);
                Vector3D directionBackward = context._cockpits[0].WorldMatrix.GetDirectionVector(Base6Directions.Direction.Backward);
                Vector3D directionUp = context._cockpits[0].WorldMatrix.GetDirectionVector(Base6Directions.Direction.Up);
                Vector3D directionRight = context._cockpits[0].WorldMatrix.GetDirectionVector(Base6Directions.Direction.Right);

                Vector3D v = context._velocityTracker.Velocity;

                // Project direction vector onto plane tangent to current position on planet
                /*Vector3D planetTangentDesiredDirection = directionForward - directionForward.Dot(context._directionFromPlanetToMe) * context._directionFromPlanetToMe;
                // todo check that desired direction norm is within some bounds
                // otherwise this projection could be numerically unstable because
                // e.g. the nose is pointing straight down
                planetTangentDesiredDirection.Normalize();

                Vector3D desiredDirection = planetTangentDesiredDirection;
                Vector3D desiredGyroDirection = planetTangentDesiredDirection;*/

                Vector3D desiredGyroDirection = -context._directionFromPlanetToMe;
                Vector3D planetTangentDesiredDirection = desiredGyroDirection; // planet tangent compensation not relevant here

                Vector3D position = context.getTargetingBlockPosition();
                //Vector3D desiredV = desiredDirection * desiredSpeed;
                //Vector3D desiredPositionDueToVelocity = _lastPosition + desiredV * timeSinceLast.TotalSeconds;
                Vector3D firingPosition = context.getFiringPosition();
                Vector3D positionErrorDirection = firingPosition - position;
                double errorMagnitude = positionErrorDirection.Normalize();
                Vector3D desiredV;
                if (errorMagnitude > 0.05)
                {
                    double desiredAcceleration = 5;
                    double desiredSpeed = Math.Sqrt(2 * desiredAcceleration * errorMagnitude);
                    desiredV = positionErrorDirection * desiredSpeed;
                }
                else
                {
                    desiredV = new Vector3D();
                }
                double t = timeSinceLast.TotalSeconds;
                Vector3 desiredPositionDueToVelocity = position + desiredV * t;


                _lastPosition = position;

                Vector3D feedforwardPosition = position + v * t + context.getAcceleration() * t * t;
                Vector3D desiredPosition = desiredPositionDueToVelocity;

                double fwdControl = updateDirectionalController(
                    context,
                    _fwdController,
                    directionForward,
                    desiredPosition,
                    feedforwardPosition,
                    timeSinceLast
                );

                double vertControl = updateDirectionalController(
                    context,
                    _vertController,
                    directionUp,
                    desiredPosition,
                    feedforwardPosition,
                    timeSinceLast
                );

                double horzControl = updateDirectionalController(
                    context,
                    _horzController,
                    directionRight,
                    desiredPosition,
                    feedforwardPosition,
                    timeSinceLast
                );

                double pitchError = signedAngleBetweenNormalizedVectors(directionForward, desiredGyroDirection, directionRight);
                double pitchControl = _pitchController.update(timeSinceLast, pitchError, pitchError);
                if (Math.Abs(pitchError) < 1e-5)
                {
                    pitchControl = 0;
                }
                context._lastPitchError_rads = pitchError;
                
                Vector3D planetTangentRollDirection = planetTangentDesiredDirection.Cross(context._directionFromPlanetToMe);
                double rollError = signedAngleBetweenNormalizedVectors(directionRight, planetTangentRollDirection, directionForward);
                double rollControl = _rollController.update(timeSinceLast, rollError, rollError);
                if (Math.Abs(rollError) < 1e-5)
                {
                    rollControl = 0;
                }
                context._lastRollError_rads = rollError;

                float gyroPower = 1;
                if (Math.Abs(pitchError) < 1e-2 && Math.Abs(rollError) < 1e-2)
                {
                    pitchControl *= 2;
                    rollControl *= 2;
                    gyroPower = 0.1f;
                }

                switch (context._debug)
                {
                    case CruiseDebug.None:
                        break;
                    case CruiseDebug.Forward:
                        displayController(context, _fwdController, "Forward");
                        break;
                    case CruiseDebug.Horizontal:
                        displayController(context, _horzController, "Horizontal");
                        break;
                    case CruiseDebug.Vertical:
                        displayController(context, _vertController, "Vertical");
                        break;
                    case CruiseDebug.Pitch:
                        displayController(context, _pitchController, "Pitch");
                        break;
                    case CruiseDebug.Roll:
                        displayController(context, _rollController, "Roll");
                        break;
                }

                float yaw = context._cockpits[0].RotationIndicator.Y / 30;

                Vector3 moveIndicator = context._cockpits[0].MoveIndicator;
                float moveVert = moveIndicator.Y;
                float moveForward = moveIndicator.Z;
                if (moveVert > 0.1)
                {
                    vertControl = 1;
                }
                else if (moveVert < -0.1)
                {
                    vertControl = -1;
                }

                if (moveForward > 0.1)
                {
                    // Stop if the user presses backwards
                    context.transition(MyContext.Stopped);
                    return;
                }
                else if (moveForward < -0.1)
                {
                    fwdControl = 1;
                }

                // Divide by 2 because it goes -180 to 180 instead of -90 to 90
                _rotationVector[0] = (float)pitchControl;
                _rotationVector[1] = yaw;
                _rotationVector[2] = (float)-rollControl / 2;
                _rotationVector[3] = -_rotationVector[0];
                _rotationVector[4] = -_rotationVector[1];
                _rotationVector[5] = -_rotationVector[2];

                foreach (Gyroscope gyro in context._compensatedGyros)
                {
                    gyro.SetRotation(_rotationVector, gyroPower);
                }


                _timeRunning += timeSinceLast;
                context._thrust.setOverrideRatio(Thrust.FWD, fwdControl);
                context._thrust.setOverrideRatio(Thrust.REV, -fwdControl);
                context._thrust.setOverrideRatio(Thrust.UP, vertControl);
                context._thrust.setOverrideRatio(Thrust.DOWN, -vertControl);
                context._thrust.setOverrideRatio(Thrust.RIGHT, horzControl);
                context._thrust.setOverrideRatio(Thrust.LEFT, -horzControl);
            }

            private void displayController(MyContext context, PidController controller, string name)
            {
                context.displayDebugText(
                        string.Format(
                            "{0}:\nP={1:0.00}\nI={2:0.00}\nD={3}\nCTRL={4}\nErr={5}\nPos={6}\nIntegral={7};\nDerivative={8}\n",
                            name,
                            controller.getPTerm(),
                            controller.getITerm(),
                            controller.getDTerm(),
                            controller.getControl(),
                            controller.getError(),
                            controller.getPosition(),
                            controller.getErrorIntegral(),
                            controller.getPositionDerivative()
                        ),
                        false
                    );
            }

            public double updateDirectionalController(
                MyContext context,
                PidController controller,
                Vector3D direction,
                Vector3D desiredPosition,
                Vector3D currentPosition,
                TimeSpan timeSinceLastUpdate
            )
            {
                Vector3D errorVector = desiredPosition - currentPosition;
                double error = errorVector.Dot(direction);
                double position = currentPosition.Dot(direction);
                double control = controller.update(timeSinceLastUpdate, error, position);
                return control;
            }

            private double signedAngleBetweenNormalizedVectors(Vector3D a, Vector3D b, Vector3D axis)
            {
                double sin = b.Cross(a).Dot(axis);
                double cos = a.Dot(b);

                return Math.Atan2(sin, cos);
            }


            private string printVector(Vector3D v)
            {
                return string.Format("X={0:0.00}, Y={1:0.00}, Z={2:0.00}", v.X, v.Y, v.Z);
            }

            public override void enter(MyContext context)
            {
                base.enter(context);

                context.updateBlocks();

                if (!context.FoundAllBlocks)
                {
                    context.transition(MyContext.Stopped);
                }

                context.Program.Runtime.UpdateFrequency = UpdateFrequency.Update1;

               // context._cockpits[0].DampenersOverride = false;

               
                _fwdController.reset();
                _vertController.reset();
                _horzController.reset();
                _pitchController.reset();
                _rollController.reset();

                Vector3D gridPosition = context.getTargetingBlockPosition();
                context._nearestPlanet.update(gridPosition, new TimeSpan(), true);
                _lastPosition = gridPosition;

                _timeRunning = new TimeSpan();
            }

            public override void leave(MyContext context)
            {
                base.leave(context);

                context.Program.Runtime.UpdateFrequency = UpdateFrequency.Update100;

                if (context._cockpits.Count > 0)
                {
                    context._cockpits[0].DampenersOverride = true;
                }

                context._thrust.resetOverrideRatio();

                foreach (IMyGyro gyro in context._gyros)
                {
                    gyro.GyroOverride = false;
                    gyro.GyroPower = 1;
                }
            }
        }


        StateMachineProgram<MyContext> _impl;

        public Program()
        {
            _impl = new StateMachineProgram<MyContext>(
                this,
                (cmd, args) => trigger(cmd, args),
                v => Storage = v
            );

            _impl.init(new MyContext(_impl));
        }

        public void trigger(string command, string[] args)
        {
            if (command == "cruise")
            {
                _impl.Context.log("Cruise control enabled");
                _impl.Context.transition(new CruiseControlState());
            }
            else if (command == "cancel_cruise")
            {
                _impl.Context.log("Cruise control disabled");
                _impl.Context.transition(MyContext.Stopped);
            }
            else if (command == "debug")
            {
                CruiseDebug debug = (CruiseDebug) Enum.Parse(typeof(CruiseDebug), args[0]);
                if (_impl.Context._debug == debug)
                {
                    // toggle
                    debug = CruiseDebug.None;
                }
                _impl.Context._debug = debug;
            }
        }

        public void Save()
        {
            _impl.Save();
        }

        public void Main(string argument, UpdateType updateSource)
        {
            _impl.Main(argument, updateSource);
        }
    }
}