using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer;
using beatleader_parser;
using Newtonsoft.Json;
using Parser.Map;
using Parser.Map.Difficulty.V3.Base;
using Ratings.AccAi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Ratings.AccAi.PerNote;
using Object = UnityEngine.Object;

namespace Ratings
{
    [Plugin("Ratings")]
    public class Plugin
    {
        private UI _ui;

        private const int EditorSceneBuildIndex = 3;
        private const int PollIntervalMilliseconds = 1000; // poll interval
        private const double PASS_CALIBRATION_FACTOR = 0.825;
        private const double BALANCED_TECH_SCALER = 14.0;

        private static readonly Parse Parser = new();
        private static readonly Analyze Analyzer = new();
        private static readonly AccRating AccRating = new();
        private static readonly Curve Curve = new();

        private readonly PerNote PerNote = new();
        private readonly Full Full = new();

        private BeatSaberSongContainer? _beatSaberSongContainer;
        private NoteGridContainer? _noteGridContainer;
        private AudioTimeSyncController? _audioTimeSyncController;
        public SongTimelineController? _songTimeLineController;
        private MapEditorUI? _mapEditorUI;

        public beatleader_analyzer.BeatmapScanner.Data.Ratings AnalyzerData = new();
        private List<NoteAcc> AccAiData = new();

        public double PredictedAcc = 0f;
        public double Acc = 0f;
        public double Tech = 0f;
        public double Pass = 0f;
        public double Star = 0f;

        public Config Config = new();
        public TextMeshProUGUI Label;

        private bool _initialized = false;
        private Scene _currentScene;

        [Init]
        private void Init()
        {
            SceneManager.sceneLoaded += SceneLoaded;
            LoadedDifficultySelectController.LoadedDifficultyChangedEvent += LoadedDifficultyChanged;
            _ui = new UI(this);
            LoadConfigFile();
        }

        private void LoadConfigFile()
        {
            string path = Path.Combine(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)), "Ratings.json");
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                Config = JsonConvert.DeserializeObject<Config>(json);
            }
        }

        public void SaveConfigFile()
        {
            string path = Path.Combine(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)), "Ratings.json");
            string json = JsonConvert.SerializeObject(Config);
            File.WriteAllText(path, json);
        }

        private void LoadedDifficultyChanged()
        {
            SceneLoaded(_currentScene, LoadSceneMode.Single);
        }

        private async void SceneLoaded(Scene arg0, LoadSceneMode arg1)
        {
            _initialized = false;
            _currentScene = arg0;

            if (_currentScene.buildIndex == EditorSceneBuildIndex)
            {
                _mapEditorUI = null;
                _noteGridContainer = null;
                _beatSaberSongContainer = null;
                _audioTimeSyncController = null;
                _songTimeLineController = null;

                await FindObject();

                BeatmapV3 map = Parser.TryLoadPath(_beatSaberSongContainer.Info.Directory);
                string characteristic = _beatSaberSongContainer.MapDifficultyInfo.Characteristic;
                string difficulty = _beatSaberSongContainer.MapDifficultyInfo.Difficulty;
                DifficultyV3 diff = map.Difficulties.FirstOrDefault(x => x.Difficulty == difficulty && x.Characteristic == characteristic).Data;

                _Difficultybeatmaps difficultyBeatmap = map.Info._difficultyBeatmapSets.FirstOrDefault(x => x._beatmapCharacteristicName == characteristic)._difficultyBeatmaps.FirstOrDefault(x => x._difficulty == difficulty);
                int diffCount = map.Info._difficultyBeatmapSets.FirstOrDefault(x => x._beatmapCharacteristicName == characteristic)._difficultyBeatmaps.Count();
                
                if (_noteGridContainer.MapObjects.Count >= 20)
                {
                    AnalyzerData = Analyzer.GetRating(diff, characteristic, difficulty, map.Info._beatsPerMinute, Config.Timescale);
                    if (AnalyzerData != null)
                    {
                        Tech = AnalyzerData.TechRating;
                        Pass = AnalyzerData.PassRating;
                        PredictedAcc = Full.GetAIAcc(diff, _beatSaberSongContainer.Info.BeatsPerMinute, Config.Timescale);
                        Acc = AccRating.GetRating(PredictedAcc, Pass, Tech);
                        Acc *= AnalyzerData.LowNoteNerf;
                        Star = Curve.ToStars(Config.StarAccuracy, Acc, Pass, Tech);
                    }
                    AccAiData = PerNote.PredictHitsForMapNotes(diff, _beatSaberSongContainer.Info.BeatsPerMinute, _beatSaberSongContainer.MapDifficultyInfo.NoteJumpSpeed, Config.Timescale);
                    _ui.ApplyNewValues();
                    _initialized = true;
                }
                else
                {
                    Debug.LogError("Ratings require 20 or more notes to analyze the map. Current note count: " + _noteGridContainer.MapObjects.Count);
                }

                _audioTimeSyncController.TimeChanged += OnTimeChanged;

                if (Label == null)
                {
                    ApplyUI();
                }

                _ui.AddMenu(_mapEditorUI);
            }
        }

        public void Reload()
        {
            _initialized = false;

            BeatmapV3 map = Parser.TryLoadPath(_beatSaberSongContainer.Info.Directory);
            string characteristic = _beatSaberSongContainer.MapDifficultyInfo.Characteristic;
            string difficulty = _beatSaberSongContainer.MapDifficultyInfo.Difficulty;
            DifficultyV3 diff = map.Difficulties.FirstOrDefault(x => x.Difficulty == difficulty && x.Characteristic == characteristic).Data;

            _Difficultybeatmaps difficultyBeatmap = map.Info._difficultyBeatmapSets.FirstOrDefault(x => x._beatmapCharacteristicName == characteristic)._difficultyBeatmaps.FirstOrDefault(x => x._difficulty == difficulty);
            int diffCount = map.Info._difficultyBeatmapSets.FirstOrDefault(x => x._beatmapCharacteristicName == characteristic)._difficultyBeatmaps.Count();

            if (_noteGridContainer.MapObjects.Count >= 20)
            {
                AnalyzerData = Analyzer.GetRating(diff, characteristic, difficulty, map.Info._beatsPerMinute, Config.Timescale);
                if (AnalyzerData != null)
                {
                    Tech = AnalyzerData.TechRating;
                    Pass = AnalyzerData.PassRating;
                    PredictedAcc = Full.GetAIAcc(diff, _beatSaberSongContainer.Info.BeatsPerMinute, Config.Timescale);
                    Acc = AccRating.GetRating(PredictedAcc, Pass, Tech);
                    Acc *= AnalyzerData.LowNoteNerf;
                    Star = Curve.ToStars(Config.StarAccuracy, Acc, Pass, Tech);
                }
                AccAiData = PerNote.PredictHitsForMapNotes(diff, _beatSaberSongContainer.Info.BeatsPerMinute, _beatSaberSongContainer.MapDifficultyInfo.NoteJumpSpeed, Config.Timescale);
                _ui.ApplyNewValues();
                _initialized = true;
            }
            else
            {
                Debug.LogError("Ratings require 20 or more notes to analyze the map. Current note count: " + _noteGridContainer.MapObjects.Count);
            }
        }

        private async Task FindObject()
        {
            while (_noteGridContainer == null || _beatSaberSongContainer == null || _audioTimeSyncController == null || _songTimeLineController == null || _mapEditorUI == null)
            {
                await Task.Delay(PollIntervalMilliseconds);
                _noteGridContainer = _noteGridContainer ?? Object.FindObjectOfType<NoteGridContainer>();
                _beatSaberSongContainer = _beatSaberSongContainer ?? Object.FindObjectOfType<BeatSaberSongContainer>();
                _audioTimeSyncController = _audioTimeSyncController ?? Object.FindObjectOfType<AudioTimeSyncController>();
                _songTimeLineController = _songTimeLineController ?? Object.FindObjectOfType<SongTimelineController>();
                _mapEditorUI = _mapEditorUI ?? Object.FindObjectOfType<MapEditorUI>();
            }
        }

        private void OnTimeChanged()
        {
            if (!Config.Enabled || !_initialized)
            {
                return;
            }

            float time = _audioTimeSyncController.CurrentSongBpmTime;
            float seconds = _audioTimeSyncController.CurrentSeconds;

            List<SwingData> timeData = AnalyzerData.SwingData.Where(x => x.BpmTime >= time).Take(Config.NotesCount).ToList();
            List<NoteAcc> accData = AccAiData.Where(x => x.time >= seconds).Take(Config.NotesCount).ToList();
            if (timeData.Count <= 1 || accData.Count <= 1)
            {
                Label.text = "Not enough data available.";
                return;
            }

            double swingDiff = timeData.Average(x => x.SwingDiff) * PASS_CALIBRATION_FACTOR;
            double swingTech = timeData.Average(x => x.SwingTech) * BALANCED_TECH_SCALER;
            double avgAcc = accData.Average(x => x.acc);

            double accRating = AccRating.GetRating(avgAcc, swingDiff, swingTech);
            double star = Curve.ToStars(Config.StarAccuracy, accRating, swingDiff, swingTech);
            
            Label.text = "Data from next " + timeData.Count.ToString("F2") + " notes ->" +
                " Pass: " + Math.Round(swingDiff, 3).ToString("F3") +
                " Tech: " + Math.Round(swingTech, 3).ToString("F3") +
                " Acc: " + Math.Round(accRating, 3).ToString("F3") + 
                " Star: " + Math.Round(star, 3).ToString("F3");
        }

        private void ApplyUI()
        {
            TextMeshProUGUI songTimeText = _songTimeLineController.transform.Find("Song Time").GetComponent<TextMeshProUGUI>();

            Label = Object.Instantiate(songTimeText, songTimeText.transform.parent);
            Label.rectTransform.localPosition = new Vector2(-210f, 36f);
            Label.alignment = TextAlignmentOptions.BottomLeft;
            Label.fontSize = 17;
            Label.text = "";
        }
    }
}
