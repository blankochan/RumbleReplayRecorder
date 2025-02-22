﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Il2CppRUMBLE.Players;
using MelonLoader;
using Newtonsoft.Json;
using RumbleModdingAPI;
using UnityEngine;

namespace RumbleReplay
{
    public sealed class RumbleReplayModClass : MelonMod
    {
        GameObject[] _poolObjects = new GameObject[8]; // Global to the class, easier to keep track of 
        readonly int[][] _cullers = new int[8][];
        List<Byte> _writebuffer = new List<Byte>();


        private MelonPreferences_Category _rumbleReplayPreferences;
        private MelonPreferences_Entry<int> _playerUpdateInterval;
        
        public bool Recording;
        public Int16 FrameCounter;
        internal string CurrentScene;
        FileStream _replayFile;
        BinaryWriter _replayWriter;
        public sealed class ReplayHeader //ignore the warnings about unused variables it gets serialized by JsonConvert.SerializeObject
        {
            public readonly string Version = "2.0.0";
            public string EnemyName;
            public string LocalName;
            public string MapName;
            public readonly string Date = DateTime.UtcNow.ToString("yyyy/MM/dd HH:mm:ss");
        }
        public void NewReplay(string scene,string localPlayerName = "", string remotePlayerName = "")
        {
            ReplayHeader replayHeader = new ReplayHeader
            {
                EnemyName = remotePlayerName,
                LocalName = localPlayerName,
                MapName = scene
            };
            string header = JsonConvert.SerializeObject(replayHeader);
            if (_replayFile != null) { StopReplay(); }
            LoggerInstance.Msg("Recording Started");
            _replayFile = File.Create($"replays/{localPlayerName}-Vs-{remotePlayerName} On {scene}-{Path.GetRandomFileName()}.rr"); 

            _replayWriter = new BinaryWriter(_replayFile);
            byte[] magicBytes = { 0x52, 0x52 }; // 'RR'

            _replayWriter.Write(magicBytes);
            _replayWriter.Write(header); // json header, first byte is str length
            Recording = true; //should get ModUI support for starting/stopping at some point 
        }
        public void StopReplay()
        {
            if (_replayFile == null) { return; }
            _replayWriter.Write(_writebuffer.ToArray());
            _writebuffer.Clear(); // Write Any Pending Data
            
            Recording = false; //should get ModUI support for starting/stopping at some point
            FrameCounter = 0;
            MelonLogger.Msg("Recording Stopped");
            _replayFile.Close();
            _replayFile = null;
            _replayWriter = null;


        }
        public override void OnLateInitializeMelon()
        {
            Calls.onMapInitialized += MapReady;
        }

        public override void OnInitializeMelon()
        {
            _rumbleReplayPreferences = MelonPreferences.CreateCategory("OurFirstCategory");
            _rumbleReplayPreferences.SetFilePath(@"UserData/RumbleReplay/RumbleReplay.cfg");
            _playerUpdateInterval = _rumbleReplayPreferences.CreateEntry("Player_Update_Interval", 4);
            _enabled = _rumbleReplayPreferences.CreateEntry("RecordingEnabled", true);
            _rumbleReplayPreferences.SaveToFile();
            
            LoggerInstance.Msg($"Player_Update_Interval={_playerUpdateInterval.Value}");
            LoggerInstance.Msg($"Enabled={_enabled.Value}");
        }
        
        public override void OnSceneWasLoaded(int _, string sceneName)
        {
            Recording = false;
            CurrentScene = sceneName;
        }
        private void MapReady()
        {
            if (CurrentScene != "Loader" && CurrentScene != "Park" && CurrentScene != "Gym")
            {
                LoggerInstance.Msg($"Loaded scene: {CurrentScene}");
                // Setup Pools Into our PoolObjects array
                _poolObjects[0] = Calls.Pools.Structures.GetPoolBall();
                _poolObjects[1] = Calls.Pools.Structures.GetPoolBoulderBall();
                _poolObjects[2] = Calls.Pools.Structures.GetPoolCube();
                _poolObjects[3] = Calls.Pools.Structures.GetPoolDisc();
                _poolObjects[4] = Calls.Pools.Structures.GetPoolLargeRock();
                _poolObjects[5] = Calls.Pools.Structures.GetPoolPillar();
                _poolObjects[6] = Calls.Pools.Structures.GetPoolWall();
                _poolObjects[7] = Calls.Pools.Structures.GetPoolSmallRock();

                
                for (UInt16 poolIndex = 0; poolIndex < _poolObjects.Length; poolIndex++)
                {
                    var pool = _poolObjects[poolIndex];
                    _cullers[poolIndex] = new int[pool.transform.GetChildCount()];
                    LoggerInstance.Msg(_cullers[poolIndex].Length);
                    LoggerInstance.Msg(pool.transform.GetChild(0).name);
                    for (UInt16 i = 0; i < pool.transform.GetChildCount(); i++)
                    {
                        GameObject structure = pool.transform.GetChild(i).gameObject;
                        _cullers[poolIndex][i] = structure.transform.position.GetHashCode();
                        

                    }
                }
                string localPlayer = Calls.Managers.GetPlayerManager().LocalPlayer.Data.GeneralData.PublicUsername;
                string remotePlayer = Calls.Players.GetEnemyPlayers().FirstOrDefault()?.Data.GeneralData.PublicUsername ?? "Unknown";
                LoggerInstance.Msg(localPlayer);
                LoggerInstance.Msg(remotePlayer);
                NewReplay(CurrentScene,Regex.Replace(localPlayer, "[^a-zA-Z0-9_ ]", ""),Regex.Replace(remotePlayer, "[^a-zA-Z0-9_ ]", ""));  
            }
            else // put our stop logic here
            {
                StopReplay();
            }
        }
        public override void OnFixedUpdate()
        {
            if ( Recording )
            {
                List<Byte> basicPlayerUpdatePartialFrame = new List<Byte>();
                if (FrameCounter % _playerUpdateInterval.Value == 0) // my hack fix for every other frame
                {
                    int index = 0;
                    foreach (Player player in Calls.Managers.GetPlayerManager().AllPlayers)
                    {
                         
                         Transform headTransform = player.Controller.transform.GetChild(5).GetChild(4).transform; // head position
                         Transform leftHandTransform = player.Controller.transform.GetChild(1).GetChild(1).transform; // Left Hand Transform
                         Transform rightHandTransform = player.Controller.transform.GetChild(1).GetChild(2).transform; // Right Hand Transform
                         
                         basicPlayerUpdatePartialFrame.Add(((byte)index)); // PlayerId
                         
                         // Head
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(headTransform.position.x)); // Position X
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(headTransform.position.y)); // Position Y
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(headTransform.position.z)); // Position Z
                         
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(headTransform.rotation.w)); // Rotation W
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(headTransform.rotation.y)); // Position Y
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(headTransform.rotation.x)); // Position X
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(headTransform.rotation.z)); // Position Z
                         
                         // Hands
                         // Left Hand
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(leftHandTransform.position.x)); // Position X
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(leftHandTransform.position.y)); // Position Y
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(leftHandTransform.position.z)); // Position Z
                         
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(leftHandTransform.rotation.w)); // Rotation W
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(leftHandTransform.rotation.y)); // Position Y
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(leftHandTransform.rotation.x)); // Position X
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(leftHandTransform.rotation.z)); // Position Z
                         
                         // Right Hand
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(rightHandTransform.position.x)); // Position X
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(rightHandTransform.position.y)); // Position Y
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(rightHandTransform.position.z)); // Position Z
                         
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(rightHandTransform.rotation.w)); // Rotation W
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(rightHandTransform.rotation.y)); // Position Y
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(rightHandTransform.rotation.x)); // Position X
                         basicPlayerUpdatePartialFrame.AddRange(BitConverter.GetBytes(rightHandTransform.rotation.z)); // Position Z
                         
                         index++;
                    }
                }
                List<Byte> objectUpdatePartialFrame = new List<Byte>();
                for (UInt16 poolIndex = 0; poolIndex < _poolObjects.Length; poolIndex++)
                {
                    var pool = _poolObjects[poolIndex];
                    for (UInt16 i = 0; i < _cullers[poolIndex].Length; i++)
                    {
                        GameObject structure = pool.transform.GetChild(i).gameObject;
                        if (_cullers[poolIndex][i] == structure.transform.position.GetHashCode()) 
                        {
                            continue;
                        } 

                        _cullers[poolIndex][i] = structure.transform.position.GetHashCode();

                        objectUpdatePartialFrame.Add(((byte)poolIndex)); // Structure Type
                        objectUpdatePartialFrame.Add(((byte)i)); // Object Index, there might be space savings here, but I don't know how to do that and its nicer looking this way.
                        Transform transform = structure.transform;
                        var transformPosition = structure.transform.position; // for some reason rider throws an error, if I don't separate it out, visual studio 2022 doesn't, but I use rider so stuck with this
                        if (structure.GetComponent<Rigidbody>().IsSleeping() && structure.transform.position.y <= 0) // rumble when it breaks something sets the Y to a number below 0 (the exact number changes on my system sometimes, but It's always below 0)
                        {
                            transformPosition.y = -300; // arbitrary, allows for a parser to see -300 and just mark it as destroyed without making the format overly complex
                        } 
                        objectUpdatePartialFrame.AddRange(BitConverter.GetBytes(transformPosition.x)); // Position X
                        objectUpdatePartialFrame.AddRange(BitConverter.GetBytes(transformPosition.y)); // Position Y
                        objectUpdatePartialFrame.AddRange(BitConverter.GetBytes(transformPosition.z)); // Position Z

                        
                        objectUpdatePartialFrame.AddRange(BitConverter.GetBytes(transform.rotation.w)); // Rotation W
                        objectUpdatePartialFrame.AddRange(BitConverter.GetBytes(transform.rotation.y)); // Position Y
                        objectUpdatePartialFrame.AddRange(BitConverter.GetBytes(transform.rotation.x)); // Position X
                        objectUpdatePartialFrame.AddRange(BitConverter.GetBytes(transform.rotation.z)); // Position Z
                    }
                }
                //Frame Header
                if (objectUpdatePartialFrame.Count != 0)
                {
                    _writebuffer.AddRange(BitConverter.GetBytes(((short)objectUpdatePartialFrame.Count))); // short in the event it's ever longer than 256 bytes, if a single frame takes 65,536 bytes something is probably wrong
                    _writebuffer.AddRange(BitConverter.GetBytes(FrameCounter));
                    _writebuffer.Add(0); // ObjectUpdate
                    _writebuffer.AddRange(objectUpdatePartialFrame);
                }
                //Frame Header
                if (basicPlayerUpdatePartialFrame.Count != 0)
                {
                    _writebuffer.AddRange(BitConverter.GetBytes(((short)basicPlayerUpdatePartialFrame.Count))); // short in the event it's ever longer than 256 bytes, if a single frame takes 65,536 bytes something is probably wrong
                    _writebuffer.AddRange(BitConverter.GetBytes(FrameCounter));
                    _writebuffer.Add(1); // BasicPlayerUpdate
                    _writebuffer.AddRange(basicPlayerUpdatePartialFrame);
                }
                FrameCounter++;



                if (_writebuffer.Count >= 1000) //1kb of replay, arbitrary, TODO replace with more complicated more time based logic
                {
                    _replayWriter.Write(_writebuffer.ToArray());
                    //LoggerInstance.Msg($"Writing {_writebuffer.Count} bytes, Frame:{FrameCounter}");
                    _writebuffer.Clear();
                   
                }

            }
        }
        public override void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.G))
            {
                // fill with debugging info or smth
            }
        }
    }
}
