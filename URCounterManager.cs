﻿using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TootTallyCore.Utils.Helpers;
using TootTallyCore.Utils.TootTallyGlobals;
using UnityEngine;

namespace TootTallyURCounter
{
    public static class URCounterManager
    {
        public static float AdjustedTimingWindow { get; private set; }

        private static bool _lastIsTooting, _isSlider, _isStarted;
        private static float _trackTime, _lastTiming, _nextTiming;
        private static int _lastIndex;
        private static bool _releasedToot;
        private static int _timingCount;
        private static float _timingSum;

        private static List<float> _noteTimingList, _tapTimingList;
        private static URCounterGraphicController _graphicController;

        [HarmonyPatch(typeof(GameController), nameof(GameController.Start))]
        [HarmonyPostfix]
        public static void OnGameControllerStart(GameController __instance)
        {
            AdjustedTimingWindow = (Plugin.Instance.TimingWindow.Value / 1000f) / TootTallyGlobalVariables.gameSpeedMultiplier;
            _isSlider = false;
            _isStarted = false;
            _lastIsTooting = false;
            _lastIndex = -1;
            _trackTime = -__instance.noteoffset - __instance.latency_offset;
            _nextTiming = __instance.leveldata.Count > 0 ? B2s(__instance.leveldata[0][0], __instance.tempo) : 0;
            _noteTimingList = new List<float>();
            _tapTimingList = new List<float>();
            _noteTimingList.Add(_nextTiming);
            _graphicController = new URCounterGraphicController(__instance.pointer.transform.parent, __instance.singlenote.transform.GetChild(3).GetComponent<LineRenderer>().material);
        }

        [HarmonyPatch(typeof(GameController), nameof(GameController.Update))]
        [HarmonyPostfix]
        public static void UpdateTrackTimer(GameController __instance)
        {
            if (_isStarted && !__instance.paused && !__instance.quitting && !__instance.retrying)
            {
                _trackTime += Time.deltaTime * TootTallyGlobalVariables.gameSpeedMultiplier;
                _graphicController.UpdateTimingBarAlpha();
            }
        }

        [HarmonyPatch(typeof(GameController), nameof(GameController.isNoteButtonPressed))]
        [HarmonyPostfix]
        public static void OnButtonPressedRegisterTapTiming(GameController __instance, bool __result)
        {
            if (!_lastIsTooting && __result && ShouldRecordTap())
                RecordTapTiming(__instance);

            _lastIsTooting = __result;
        }

        [HarmonyPatch(typeof(GameController), nameof(GameController.playsong))]
        [HarmonyPostfix]
        public static void OnPlaySong() => _isStarted = true;

        public static bool ShouldRecordTap() => _noteTimingList.Count > _tapTimingList.Count;

        public static void RecordTapTiming(GameController __instance)
        {
            var msTiming = _nextTiming - _trackTime;
            if (msTiming == 0)
            {
                //Plugin.LogInfo($"Tap was perfectly on time.");
                OnTapRecord(0);
            }
            else if (Mathf.Abs(msTiming) <= AdjustedTimingWindow)
            {
                //Plugin.LogInfo($"Tap was {(Math.Sign(msTiming) == 1 ? "early" : "late")} by {msTiming / TootTallyGlobalVariables.gameSpeedMultiplier}ms");
                OnTapRecord(msTiming / TootTallyGlobalVariables.gameSpeedMultiplier);
            }
        }

        public static void OnTapRecord(float tapTiming)
        {
            _tapTimingList.Add(tapTiming);
            _graphicController.AddTiming(tapTiming);
            _timingSum += tapTiming;
            _timingCount++;
            _graphicController.SetAveragePosition(_timingSum / _timingCount);
        }

        [HarmonyPatch(typeof(GameController), nameof(GameController.syncTrackPositions))]
        [HarmonyPostfix]
        public static void OnSyncTrack(GameController __instance) =>
                _trackTime = ((float)__instance.musictrack.timeSamples / __instance.musictrack.clip.frequency) - __instance.noteoffset - __instance.latency_offset;

        public static float B2s(float time, float bpm) => time / bpm * 60f;

        [HarmonyPatch(typeof(GameController), nameof(GameController.getScoreAverage))]
        [HarmonyPrefix]
        public static void GetNoteScore(GameController __instance)
        {
            _releasedToot = __instance.released_button_between_notes && !__instance.force_no_gap_gameobject_to_appear;
        }

        [HarmonyPatch(typeof(GameController), nameof(GameController.grabNoteRefs))]
        [HarmonyPrefix]
        public static void GetIsSlider(GameController __instance)
        {
            if (__instance.currentnoteindex + 1 >= __instance.leveldata.Count) return;
            if (!_isStarted)
            {
                _noteTimingList = new List<float>();
                _tapTimingList = new List<float>();
            }


            _isSlider = Mathf.Abs(__instance.leveldata[__instance.currentnoteindex + 1][0] - (__instance.leveldata[__instance.currentnoteindex][0] + __instance.leveldata[__instance.currentnoteindex][1])) < 0.05f;

            if (!_isSlider)
            {
                if (_noteTimingList.Count > _tapTimingList.Count)
                    if (!_releasedToot)
                    {
                        //Plugin.LogInfo($"Tap was not released: {Mathf.Max(_lastTiming - _nextTiming, -AdjustedTimingWindow)}");
                        OnTapRecord(Mathf.Max(_lastTiming - _nextTiming, -AdjustedTimingWindow) / TootTallyGlobalVariables.gameSpeedMultiplier);
                    }
                    else
                    {
                        //Plugin.LogInfo($"Tap not registered: {Mathf.Min(_trackTime - _nextTiming, AdjustedTimingWindow)}");
                        OnTapRecord(Mathf.Min(_trackTime - _nextTiming, AdjustedTimingWindow) / TootTallyGlobalVariables.gameSpeedMultiplier);
                    }

                _lastTiming = _nextTiming;
                _nextTiming = B2s(__instance.leveldata[__instance.currentnoteindex + 1][0], __instance.tempo);
                _noteTimingList.Add(_nextTiming);
            }
        }

        public static double GetStandardDeviation(List<float> values)
        {
            var average = values.Average();
            return Mathf.Sqrt(values.Average(value => FastPow(value - average, 2)));
        }

        public static float FastPow(double num, int exp)
        {
            double result = 1d;
            while (exp > 0)
            {
                if (exp % 2 == 1)
                    result *= num;
                exp >>= 1;
                num *= num;
            }
            return (float)result;
        }
    }
}