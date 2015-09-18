﻿using System;
using CSGO_Demos_Manager.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using CSGO_Demos_Manager.Models.Charts;
using CSGO_Demos_Manager.Models.Source;
using CSGO_Demos_Manager.Models.Stats;
using CSGO_Demos_Manager.Properties;
using CSGO_Demos_Manager.Services.Analyzer;
using CSGO_Demos_Manager.Services.Serialization;
using MoreLinq;
using Newtonsoft.Json;

namespace CSGO_Demos_Manager.Services
{
	public class DemosService : IDemosService
	{
		private readonly CacheService _cacheService = new CacheService();

		private readonly ISteamService _steamService;

		public DemosService(ISteamService steamService)
		{
			_steamService = steamService;
		}

		/// <summary>
		/// Check if there are banned players and update their banned flags
		/// </summary>
		/// <param name="demo"></param>
		/// <returns></returns>
		public async Task<Demo> AnalyzeBannedPlayersAsync(Demo demo)
		{
			List<string> ids = demo.Players.Select(playerExtended => playerExtended.SteamId.ToString()).ToList();
			IEnumerable<Suspect> suspects = await _steamService.GetBanStatusForUserList(ids);
			var enumerableSuspects = suspects as IList<Suspect> ?? suspects.ToList();
			if (enumerableSuspects.Any())
			{
				// Update player's flag
				foreach (Suspect suspect in enumerableSuspects)
				{
					PlayerExtended cheater = demo.Players.FirstOrDefault(p => p.SteamId.ToString() == suspect.SteamId);
					if (cheater != null)
					{
						if (suspect.CommunityBanned)
						{
							demo.HasCheater = true;
							cheater.IsOverwatchBanned = true;
						}
						if (suspect.VacBanned)
						{
							demo.HasCheater = true;
							cheater.IsVacBanned = true;
						}
					}
				}
			}
			return demo;
		}

		private async Task<Demo> GetDemoHeaderAsync(string demoFilePath)
		{
			var demo = await Task.Run(() => DemoAnalyzer.ParseDemoHeader(demoFilePath));
			if (demo == null) return null;

			// If demo is in cache we retrieve its data
			bool demosIsCached = _cacheService.HasDemoInCache(demo);
			if (demosIsCached)
			{
				demo = await _cacheService.GetDemoDataFromCache(demo);
				demo.Source = Source.Factory(demo.SourceName);
				// Update the demo name and path because it may be renamed / moved
				demo.Name = Path.GetFileName(demoFilePath);
				demo.Path = demoFilePath;
			}
			return demo;
		}

		public async Task<List<Demo>> GetDemosHeader(List<string> folders)
		{
			List<Demo> demos = new List<Demo>();

			if (folders.Count > 0)
			{
				foreach (string folder in folders)
				{
					if (Directory.Exists(folder))
					{
						string[] files = Directory.GetFiles(folder, "*.dem");
						foreach (string file in files)
						{
							var demo = await GetDemoHeaderAsync(file);
							if (demo != null && !demos.Contains(demo)) demos.Add(demo);
						}
					}
				}
			}
			return demos;
		}

		public async Task<Demo> AnalyzeDemo(Demo demo)
		{
			if (!File.Exists(demo.Path))
			{
				// Demo may be moved to an other folder, just clear cache
				await _cacheService.RemoveDemo(demo);
			}

			DemoAnalyzer analyzer = DemoAnalyzer.Factory(demo);
			
			demo = await analyzer.AnalyzeDemoAsync();

			return demo;
		}

		public async Task SaveComment(Demo demo, string comment)
		{
			demo.Comment = comment;
			await _cacheService.WriteDemoDataCache(demo);
		}

		public async Task SaveStatus(Demo demo, string status)
		{
			demo.Status = status;
			await _cacheService.WriteDemoDataCache(demo);
		}

		public async Task SetSource(ObservableCollection<Demo> demos, string source)
		{
			foreach (Demo demo in demos.Where(demo => demo.Type != "POV"))
			{
				switch (source)
				{
					case "valve":
						demo.Source = new Valve();
						break;
					case "esea":
						demo.Source = new Esea();
						break;
					case "ebot":
						demo.Source = new Ebot();
						break;
					case "faceit":
						demo.Source = new Faceit();
						break;
					case "cevo":
						demo.Source = new Cevo();
						break;
					default:
						demo.Source = new Valve();
						break;
				}
				await _cacheService.WriteDemoDataCache(demo);
			}
		}

		public async Task<Demo> AnalyzePlayersPosition(Demo demo)
		{
			if (!File.Exists(demo.Path))
			{
				// Demo may be moved to an other folder, just clear cache
				await _cacheService.RemoveDemo(demo);
			}

			DemoAnalyzer analyzer = DemoAnalyzer.Factory(demo);
			analyzer.AnalyzePlayersPosition = true;

			demo = await analyzer.AnalyzeDemoAsync();

			await _cacheService.WriteDemoDataCache(demo);

			return demo;
		}

		public async Task<Demo> AnalyzeHeatmapPoints(Demo demo)
		{
			if (!File.Exists(demo.Path))
			{
				// Demo may be moved to an other folder, just clear cache
				await _cacheService.RemoveDemo(demo);
			}

			DemoAnalyzer analyzer = DemoAnalyzer.Factory(demo);
			analyzer.AnalyzeHeatmapPoint = true;

			demo = await analyzer.AnalyzeDemoAsync();

			await _cacheService.WriteDemoDataCache(demo);

			return demo;
		}

		/// <summary>
		/// Return demos from JSON backup file
		/// </summary>
		/// <returns></returns>
		public async Task<List<Demo>> GetDemosFromBackup(string jsonFile)
		{
			List<Demo> demos = new List<Demo>();
			if (File.Exists(jsonFile))
			{
				string json = File.ReadAllText(jsonFile);
				demos = await Task.Factory.StartNew(() => JsonConvert.DeserializeObject<List<Demo>>(json, new DemoListBackupConverter()));
			}
			return demos;
		}

		public async Task<Rank> GetLastRankAccountStatsAsync()
		{
			Rank rank = AppSettings.RankList[0];
			List<Demo> demos = await _cacheService.GetDemoListAsync();
			if (demos.Any())
			{
				// demos where account played
				List<Demo> demosPlayerList = demos.Where(demo => demo.Players.FirstOrDefault(p => p.SteamId == Settings.Default.SelectedStatsAccountSteamID) != null).ToList();
				if (demosPlayerList.Any())
				{
					Demo lastDemo = demosPlayerList.MaxBy(d => d.Date);
					int rankNumber = lastDemo.Players.First(p => p.SteamId == Settings.Default.SelectedStatsAccountSteamID).RankNumberNew;
					rank = AppSettings.RankList.First(r => r.Number == rankNumber);
				}
			}

			return rank;
		}

		public async Task<List<RankDateChart>> GetRankDateChartDataAsync()
		{
			List<RankDateChart> datas = new List<RankDateChart>();
			List<Demo> demos = await _cacheService.GetDemoListAsync();

			if (demos.Any())
			{
				List<Demo> demosPlayerList = demos.Where(demo => demo.Players.FirstOrDefault(p => p.SteamId == Settings.Default.SelectedStatsAccountSteamID) != null).ToList();
				if (demosPlayerList.Any())
				{
					// Sort by date
					demosPlayerList.Sort((d1, d2) => d1.Date.CompareTo(d2.Date));
					foreach (Demo demo in demosPlayerList)
					{
						// Ignore demos where all players have no rank, sometimes CCSUsrMsg_ServerRankUpdate isn't raised
						if (demo.Players.All(p => p.RankNumberOld != 0))
						{
							int rankNumber = demo.Players.First(p => p.SteamId == Settings.Default.SelectedStatsAccountSteamID).RankNumberNew;
							datas.Add(new RankDateChart
							{
								Date = demo.Date,
								Rank = AppSettings.RankList.First(r => r.Number == rankNumber).Number
							});
						}
					}
				}
			}

			return datas;
		}

		public async Task<OverallStats> GetGeneralAccountStatsAsync()
		{
			OverallStats stats = new OverallStats();

			List<Demo> demos = await _cacheService.GetDemoListAsync();

			if (demos.Any())
			{
				List<Demo> demosPlayerList = demos.Where(demo => demo.Players.FirstOrDefault(p => p.SteamId == Settings.Default.SelectedStatsAccountSteamID) != null).ToList();
				if (demosPlayerList.Any())
				{
					stats.MatchCount = demosPlayerList.Count;
					foreach (Demo demo in demosPlayerList)
					{
						stats.KillCount += demo.TotalKillSelectedAccountCount;
						stats.AssistCount += demo.AssistSelectedAccountCount;
						stats.DeathCount += demo.DeathSelectedAccountCount;
						stats.KnifeKillCount += demo.KnifeKillSelectedAccountCount;
						stats.EntryKillCount += demo.EntryKillSelectedAccountCount;
						stats.FiveKillCount += demo.FiveKillSelectedAccountCount;
						stats.FourKillCount += demo.FourKillSelectedAccountCount;
						stats.ThreeKillCount += demo.ThreeKillSelectedAccountCount;
						stats.TwoKillCount += demo.TwoKillSelectedAccountCount;
						stats.HeadshotCount += demo.HeadshotSelectedAccountCount;
						stats.BombDefusedCount += demo.BombDefusedSelectedAccountCount;
						stats.BombExplodedCount += demo.BombExplodedSelectedAccountCount;
						stats.BombPlantedCount += demo.BombPlantedSelectedAccountCount;
						stats.MvpCount += demo.MvpSelectedAccountCount;
						stats.DamageCount += demo.TotalDamageHealthSelectedAccountCount;
						switch (demo.MatchVerdictSelectedAccountCount)
						{
							case -1:
								stats.MatchLossCount++;
								break;
							case 0:
								stats.MatchDrawCount++;
								break;
							case 1:
								stats.MatchWinCount++;
								break;
						}
					}
				}

				if (stats.KillCount != 0 && stats.DeathCount != 0)
				{
					stats.KillDeathRatio = Math.Round(stats.KillCount / (decimal)stats.DeathCount, 2);
				}
				if (stats.KillCount != 0 && stats.HeadshotCount != 0)
				{
					stats.HeadshotRatio = Math.Round(((decimal)stats.HeadshotCount * 100) / stats.KillCount, 2);
				}
			}

			return stats;
		}

		public async Task<MapStats> GetMapStatsAsync()
		{
			MapStats stats = new MapStats();

			List<Demo> demos = await _cacheService.GetDemoListAsync();

			if (demos.Any())
			{
				List<Demo> demosPlayerList = demos.Where(demo => demo.Players.FirstOrDefault(p => p.SteamId == Settings.Default.SelectedStatsAccountSteamID) != null).ToList();
				if (demosPlayerList.Any())
				{
					stats.Dust2WinCount = demosPlayerList.Count(d => d.MapName == "de_dust2" && d.MatchVerdictSelectedAccountCount == 1);
					stats.Dust2LossCount = demosPlayerList.Count(d => d.MapName == "de_dust2" && d.MatchVerdictSelectedAccountCount == -1);
					stats.Dust2DrawCount = demosPlayerList.Count(d => d.MapName == "de_dust2" && d.MatchVerdictSelectedAccountCount == 0);
					int matchCount = stats.Dust2WinCount + stats.Dust2LossCount + stats.Dust2DrawCount;
					if (matchCount > 0)
					{
						stats.Dust2WinPercentage = Math.Round((stats.Dust2WinCount / (double)matchCount * 100), 2);
					}

					stats.MirageWinCount = demosPlayerList.Count(d => d.MapName == "de_mirage" && d.MatchVerdictSelectedAccountCount == 1);
					stats.MirageLossCount = demosPlayerList.Count(d => d.MapName == "de_mirage" && d.MatchVerdictSelectedAccountCount == -1);
					stats.MirageDrawCount = demosPlayerList.Count(d => d.MapName == "de_mirage" && d.MatchVerdictSelectedAccountCount == 0);
					matchCount = stats.MirageWinCount + stats.MirageLossCount + stats.MirageDrawCount;
					if (matchCount > 0)
					{
						stats.MirageWinPercentage = Math.Round((stats.MirageWinCount / (double)matchCount * 100), 2);
					}

					stats.InfernoWinCount = demosPlayerList.Count(d => d.MapName == "de_inferno" && d.MatchVerdictSelectedAccountCount == 1);
					stats.InfernoLossCount = demosPlayerList.Count(d => d.MapName == "de_inferno" && d.MatchVerdictSelectedAccountCount == -1);
					stats.InfernoDrawCount = demosPlayerList.Count(d => d.MapName == "de_inferno" && d.MatchVerdictSelectedAccountCount == 0);
					matchCount = stats.InfernoWinCount + stats.InfernoLossCount + stats.InfernoDrawCount;
					if (matchCount > 0)
					{
						stats.InfernoWinPercentage = Math.Round((stats.InfernoWinCount / (double)matchCount * 100), 2);
					}

					stats.TrainWinCount = demosPlayerList.Count(d => d.MapName == "de_train" && d.MatchVerdictSelectedAccountCount == 1);
					stats.TrainLossCount = demosPlayerList.Count(d => d.MapName == "de_train" && d.MatchVerdictSelectedAccountCount == -1);
					stats.TrainDrawCount = demosPlayerList.Count(d => d.MapName == "de_train" && d.MatchVerdictSelectedAccountCount == 0);
					matchCount = stats.TrainWinCount + stats.TrainLossCount + stats.TrainDrawCount;
					if (matchCount > 0)
					{
						stats.TrainWinPercentage = Math.Round((stats.TrainWinCount / (double)matchCount * 100), 2);
					}

					stats.OverpassWinCount = demosPlayerList.Count(d => d.MapName == "de_overpass" && d.MatchVerdictSelectedAccountCount == 1);
					stats.OverpassLossCount = demosPlayerList.Count(d => d.MapName == "de_overpass" && d.MatchVerdictSelectedAccountCount == -1);
					stats.OverpassDrawCount = demosPlayerList.Count(d => d.MapName == "de_overpass" && d.MatchVerdictSelectedAccountCount == 0);
					matchCount = stats.OverpassWinCount + stats.OverpassLossCount + stats.OverpassDrawCount;
					if (matchCount > 0)
					{
						stats.OverpassWinPercentage = Math.Round((stats.OverpassWinCount / (double)matchCount * 100), 2);
					}

					stats.CacheWinCount = demosPlayerList.Count(d => d.MapName == "de_cache" && d.MatchVerdictSelectedAccountCount == 1);
					stats.CacheLossCount = demosPlayerList.Count(d => d.MapName == "de_cache" && d.MatchVerdictSelectedAccountCount == -1);
					stats.CacheDrawCount = demosPlayerList.Count(d => d.MapName == "de_cache" && d.MatchVerdictSelectedAccountCount == 0);
					matchCount = stats.CacheWinCount + stats.CacheLossCount + stats.CacheDrawCount;
					if (matchCount > 0)
					{
						stats.CacheWinPercentage = Math.Round((stats.CacheWinCount / (double)matchCount * 100), 2);
					}

					stats.CobblestoneWinCount = demosPlayerList.Count(d => d.MapName == "de_cbble" && d.MatchVerdictSelectedAccountCount == 1);
					stats.CobblestoneLossCount = demosPlayerList.Count(d => d.MapName == "de_cbble" && d.MatchVerdictSelectedAccountCount == -1);
					stats.CobblestoneDrawCount = demosPlayerList.Count(d => d.MapName == "de_cbble" && d.MatchVerdictSelectedAccountCount == 0);
					matchCount = stats.CobblestoneWinCount + stats.CobblestoneLossCount + stats.CobblestoneDrawCount;
					if (matchCount > 0)
					{
						stats.CobblestoneWinPercentage = Math.Round((stats.CobblestoneWinCount / (double)matchCount * 100), 2);
					}

					stats.NukeWinCount = demosPlayerList.Count(d => d.MapName == "de_nuke" && d.MatchVerdictSelectedAccountCount == 1);
					stats.NukeLossCount = demosPlayerList.Count(d => d.MapName == "de_nuke" && d.MatchVerdictSelectedAccountCount == -1);
					stats.NukeDrawCount = demosPlayerList.Count(d => d.MapName == "de_nuke" && d.MatchVerdictSelectedAccountCount == 0);
					matchCount = stats.NukeWinCount + stats.NukeLossCount + stats.NukeDrawCount;
					if (matchCount > 0)
					{
						stats.NukeWinPercentage = Math.Round((stats.NukeWinCount / (double)matchCount * 100), 2);
					}
				}
			}

			return stats;
		}
	}
}
