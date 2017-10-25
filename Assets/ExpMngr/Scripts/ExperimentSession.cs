﻿using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Collections.Specialized;

namespace ExpMngr
{
    /// <summary>
    /// The main class used to manage your experiment. Attach this to a gameobject, and it will manage your experiment "session".
    /// </summary>
    public class ExperimentSession : MonoBehaviour
    {

        [SerializeField] private string _expName = "experiment_name";
        /// <summary>
        /// Enable to automatically safely end the session when the application stops running.
        /// </summary>
        public bool endExperimentOnQuit = true;
        /// <summary>
        /// Name of the experiment (will be used for the generated folder name)
        /// </summary>
        public string expName {
            get { return _expName; }
            set { expName = GetSafeFilename(value); }
        }

        /// <summary>
        /// List of blocks for this experiment
        /// </summary>
        [HideInInspector]
        public List<Block> blocks = new List<Block>();

        // serialzed private + public getter trick allows setting in inspector without being publicly settable
        [SerializeField] private List<Tracker> _trackedObjects = new List<Tracker>();
        /// <summary>
        /// List of tracked objects. Add a tracker to a gameobject and set it here to track position and rotation of the object.
        /// </summary>
        public List<Tracker> trackedObjects { get { return _trackedObjects; } }

        [SerializeField] private List<string> _customHeaders = new List<string>();
        /// <summary>
        /// List of variables you want to measure in your experiment. Once set here, you can add the observations to your results disctionary on each trial.
        /// </summary>
        public List<string> customHeaders { get { return _customHeaders; } }

        [SerializeField] private List<string> _settingsToLog = new List<string>();
        /// <summary>
        /// List of settings you wish to log to the output file for each trial.
        /// </summary>
        public List<string> settingsToLog { get { return _settingsToLog; } }

        bool hasInitialised = false;

        FillableFormController formController;

        /// <summary>
        /// Settings for the experiment. These are automatically loaded from file on initialisation of the session.
        /// </summary>
        public Settings settings;

        /// <summary>
        /// Returns true if current trial is in progress
        /// </summary>
        public bool inTrial { get { return (trialNum != 0) && (currentTrial.status == TrialStatus.InProgress); } }

        /// <summary>
        /// Alias of GetTrial()
        /// </summary>
        public Trial currentTrial { get { return GetTrial(); } }

        /// <summary>
        /// Alias of NextTrial()
        /// </summary>
        public Trial nextTrial { get { return NextTrial(); } }

        /// <summary>
        /// Alias of PrevTrial()
        /// </summary>
        public Trial prevTrial { get { return PrevTrial(); } }

        /// <summary>
        /// Alias of LastTrial()
        /// </summary>
        public Trial lastTrial { get { return LastTrial(); } }

        /// <summary>
        /// Alias of GetBlock()
        /// </summary>
        public Block currentBlock { get { return GetBlock(); } }

        /// <summary>
        /// return trials for all blocks
        /// </summary>
        public List<Trial> trials
        {
            get
            {
                List<Trial> ts = new List<Trial>();
                foreach (Block block in blocks)
                    ts.AddRange(block.trials);
                return ts;
            }
        }

        /// <summary>
        /// Unique string for this session (participant ID, etc)
        /// </summary>
        [HideInInspector]
        public string sessionID;

        /// <summary>
        /// Currently active trial number.
        /// </summary>
        [HideInInspector]
        public int trialNum = 0;

        /// <summary>
        /// Currently active block number.
        /// </summary>
        [HideInInspector]
        public int blockNum = 0;

        FileIOManager fileIOManager;

        List<string> baseHeaders = new List<string> { "session_id", "trial_num", "block_num", "trial_num_in_block", "start_time", "end_time" };

        string basePath;

        /// <summary>
        /// Path to the folder used for readijng settings and storing the output. 
        /// </summary>
        public string experimentPath { get { return Path.Combine(basePath, expName); } }
        /// <summary>
        /// Path within the experiment path for this particular session.
        /// </summary>
        public string sessionPath { get { return Path.Combine(experimentPath, sessionID); } }
        /// <summary>
        /// Path within the experiment path that points to the settings json file.
        /// </summary>
        public string settingsPath { get { return Path.Combine(experimentPath, "settings.json"); } }
        /// <summary>
        /// List of file headers generated based on tracked objects.
        /// </summary>
        public List<string> trackingHeaders { get { return trackedObjects.Select(t => t.objectNameHeader).ToList(); } }
        /// <summary>
        /// Stores combined list of headers.
        /// </summary>
        [HideInInspector] public List<string> headers;

        void Start()
        {
            // start FileIOManager
            fileIOManager = new FileIOManager(this);

            // error checks (has set save folder, etc)     
            InitFolder();

            // load experiment settings
            settings = ReadSettings();

            // create headers
            headers = baseHeaders.Concat(customHeaders).Concat(trackingHeaders).Concat(settingsToLog).ToList<string>();
        }

        internal List<Tracker> GetTrackedObjects()
        {
            return trackedObjects;
        }

        void InitFolder()
        {
            if (!System.IO.Directory.Exists(Application.streamingAssetsPath))
                System.IO.Directory.CreateDirectory(Application.streamingAssetsPath);

            if (!System.IO.Directory.Exists(experimentPath))
                System.IO.Directory.CreateDirectory(experimentPath);
        }

        /// <summary>
        /// Save tracking data for this trial
        /// </summary>
        /// <param name="objectName"></param>
        /// <param name="data"></param>
        /// <returns>Path to the file</returns>
        public string SaveTrackingData(string objectName, List<float[]> data)
        {
            string fname = string.Format("movement_{0}_T{1:000}.csv", objectName, trialNum);
            string fpath = Path.Combine(sessionPath, fname);

            fileIOManager.Manage(new FileIOCommand(FileIOFunction.WriteMovementData, data, fpath));

            // return relative path so it can be saved
            Uri fullPath = new Uri(fpath);
            Uri basePath = new Uri(experimentPath);
            return basePath.MakeRelativeUri(fullPath).ToString();
        }

        /// <summary>
        /// Copies a file to the folder for this session
        /// </summary>
        /// <param name="filePath"></param>
        public void CopyFileToSessionFolder(string filePath)
        {
            string newPath = Path.Combine(sessionPath, Path.GetFileName(filePath));
            fileIOManager.Manage(new FileIOCommand(FileIOFunction.CopyFile, filePath, newPath));
        }

        /// <summary>
        /// Copies a file to the folder for this session
        /// </summary>
        /// <param name="filePath">Path to the file to copy to the session folder</param>
        /// <param name="newName">New name of the file</param>
        public void CopyFileToSessionFolder(string filePath, string newName)
        {
            string newPath = Path.Combine(sessionPath, newName);
            fileIOManager.Manage(new FileIOCommand(FileIOFunction.CopyFile, filePath, newPath));
        }

        /// <summary>
        /// Write a dictionary object to a JSON file in the session folder
        /// </summary>
        /// <param name="dict">Dictionary object to write</param>
        /// <param name="objectName">Name of the object (is used for file name)</param>
        public void WriteDictToSessionFolder(Dictionary<string, object> dict, string objectName)
        {
            string fileName = string.Format("{0}.json", objectName);
            string filePath = Path.Combine(sessionPath, fileName);
            fileIOManager.Manage(new FileIOCommand(FileIOFunction.WriteJson, filePath, dict));
        }



        /// <summary>
        /// Initialises a session with given name and writes info about the session to file
        /// </summary>
        /// <param name="sessionIdentifier">Unique ID used to identify the session.</param>
        /// <param name="sessionInfo">A dictionary of objects (such as collected demographics from a participant) to write to the session folder.</param>
        public void InitSession(string sessionIdentifier, Dictionary<string, object> sessionInfo)
        {
            InitSession(sessionIdentifier);
            WriteDictToSessionFolder(sessionInfo, "info");
        }

        /// <summary>
        /// Initialises a session with given name
        /// </summary>
        /// <param name="sessionIdentifier"></param>
        public void InitSession(string sessionIdentifier)
        {
            sessionID = GetSafeFilename(sessionIdentifier);
            if (!System.IO.Directory.Exists(sessionPath))
                System.IO.Directory.CreateDirectory(sessionPath);
            else
                Debug.LogError("Warning session already exists! Continuing will overwrite");
            hasInitialised = true;
        }

        Settings ReadSettings()
        {
            Dictionary<string, object> dict;
            try
            {
                string dataAsJson = File.ReadAllText(settingsPath);
                dict = MiniJSON.Json.Deserialize(dataAsJson) as Dictionary<string, object>;
            }
            catch (FileNotFoundException)
            {
                string message = string.Format("Settings .json file not found! Creating an empty one in {0}.", settingsPath);
                Debug.LogError(message);

                // write empty settings to experiment folder
                dict = new Dictionary<string, object>();
                fileIOManager.Manage(new FileIOCommand(FileIOFunction.WriteJson, settingsPath, dict));
            }            
            return new Settings(dict);
        }

        /// <summary>
        /// Get currently active trial.
        /// </summary>
        /// <returns>Currently active trial.</returns>
        public Trial GetTrial()
        {
            if (trialNum == 0)
            {
                throw new NoSuchTrialException("There is no trial zero. If you are the start of the experiment please use nextTrial to get the first trial");
            }
            return trials[trialNum - 1];
        }

        /// <summary>
        /// Get trial by trial number (non zero indexed)
        /// </summary>
        /// <returns></returns>
        public Trial GetTrial(int trialNumber)
        {
            return trials[trialNumber - 1];
        }

        /// <summary>
        /// Get next Trial
        /// </summary>
        /// <returns></returns>
        public Trial NextTrial()
        {
            // non zero indexed
            try
            {
                return trials[trialNum];
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new NoSuchTrialException("There is no next trial. Reached the end of trial list.");
            }
        }

        /// <summary>
        /// Get previous Trial
        /// </summary>
        /// <returns></returns>
        public Trial PrevTrial()
        {
            // non zero indexed
            try
            {
                return trials[trialNum - 2];
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new NoSuchTrialException("There is no previous trial. Probably currently at the start of experiment.");
            }
        }

        /// <summary>
        /// Get last Trial in experiment
        /// </summary>
        /// <returns></returns>
        public Trial LastTrial()
        {
            return trials[trials.Count - 1];
        }

        /// <summary>
        /// Get currently active block.
        /// </summary>
        /// <returns>Currently active block.</returns>
        public Block GetBlock()
        {
            return blocks[blockNum - 1];
        }

        /// <summary>
        /// Get block by block number (non-zero indexed).
        /// </summary>
        /// <returns>Currently active block.</returns>
        public Block GetBlock(int blockNumber)
        {
            return blocks[blockNumber - 1];
        }

        static string GetSafeFilename(string filename)
        {
            return string.Join("", filename.Split(Path.GetInvalidFileNameChars()));
        }


        /// <summary>
        /// Ends the experiment session.
        /// </summary>
        public void EndExperiment()
        {
            if (hasInitialised)
            {
                if (inTrial)
                    currentTrial.End();
                SaveResults();
                fileIOManager.Manage(new FileIOCommand(FileIOFunction.Quit));
            }
        }

        void SaveResults()
        {
            List<OrderedResultDict> results = trials.Select(t => t.result).ToList();
            string fileName = "trial_results.csv";
            string filePath = Path.Combine(sessionPath, fileName);
            fileIOManager.Manage(new FileIOCommand(FileIOFunction.WriteTrials, results, filePath));
        }


        void OnDestroy()
        {
            if (endExperimentOnQuit)
            {
                EndExperiment();
            }

        }

    }

    /// <summary>
    /// Class which represents a command sent to the FileIO manager
    /// </summary>
    public class FileIOCommand
    {
        /// <summary>
        /// Function the command should run
        /// </summary>
        public FileIOFunction function;
        /// <summary>
        /// Set of parameters to be sent to the function
        /// </summary>
        public object[] parameters;

        /// <summary>
        /// Creates a FileIOCommand for given function and paremeters
        /// </summary>
        /// <param name="func">Function</param>
        /// <param name="funcParameters">Parameters to be sent to function</param>
        public FileIOCommand(FileIOFunction func, params object[] funcParameters)
        {
            function = func;
            parameters = funcParameters;
        }
    }

    /// <summary>
    /// Enum that maps onto various functions used in the FileIO manager
    /// </summary>
    public enum FileIOFunction
    {
        CopyFile, WriteTrials, WriteJson, WriteMovementData, Quit
    }

    /// <summary>
    /// Exception thrown in cases where we try to access a trial that does not exist.
    /// </summary>
    public class NoSuchTrialException : Exception
    {
        public NoSuchTrialException()
        {
        }

        public NoSuchTrialException(string message)
            : base(message)
        {
        }

        public NoSuchTrialException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }


}

