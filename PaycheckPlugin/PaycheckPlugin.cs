﻿using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhaserArray.PaycheckPlugin.Serialization;
using PhaserArray.PaycheckPlugin.Helpers;
using Rocket.API;
using Rocket.API.Collections;
using Rocket.Core.Plugins;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace PhaserArray.PaycheckPlugin
{
    public class PaycheckPlugin : RocketPlugin<PaycheckPluginConfiguration>
    {
	    public static PaycheckPlugin Instance;
	    public static PaycheckPluginConfiguration Config;
		public const string Version = "v1.4";

	    private float _nextPaycheck;
	    private Dictionary<CSteamID, Vector3> _playerPositions;

	    public float SecondsToNextPaycheck => _nextPaycheck - Time.realtimeSinceStartup;

	    protected override void Load()
	    {
			Logger.Log($"Loading PhaserArray's Paycheck Plugin {Version}");
		    Instance = this;
		    Config = Configuration.Instance;
			_playerPositions = new Dictionary<CSteamID, Vector3>();
		    StartCoroutine(PaycheckGiver(Config.Interval));
	    }

		protected override void Unload()
		{
			Logger.Log($"Unloading PhaserArray's Paycheck Plugin {Version}");
			if (Config.IsDirty)
			{
				Logger.Log("Configuration has been changed in-game, saving!");
				Configuration.Save();
			}
			StopCoroutine(PaycheckGiver(Config.Interval));
		}

		/// <summary>
		/// Repeats the paycheck giving process every interval.
		/// </summary>
		/// <param name="interval">int</param>
	    private IEnumerator PaycheckGiver(float interval)
		{
			do
			{
				_nextPaycheck = Time.realtimeSinceStartup + interval;
				yield return new WaitForSecondsRealtime(interval);
				GiveAllPaychecks();
			} while (State == PluginState.Loaded);
		}

		/// <summary>
		/// Gives all players paychecks.
		/// </summary>
	    public void GiveAllPaychecks()
	    {
			if (!Provider.isInitialized || !Level.isLoaded) return;
		    foreach (var client in Provider.clients)
		    {
			    GivePaycheck(UnturnedPlayer.FromSteamPlayer(client));
			}
		}

		/// <summary>
		/// Gives the player their paycheck(s) w/ multipliers applied, also send paycheck notifications.
		/// </summary>
		/// <param name="player"></param>
	    public void GivePaycheck(UnturnedPlayer player)
		{
			var paychecks = GetAvailablePaychecks(player);
			if (paychecks.Count == 0)
			{
				return;
			}
			if (!Config.AllowPaychecksWhenDead && player.Dead)
			{
				ShowNotification(player, Translate("paycheck_dead"), Color.yellow);
			    return;
			}
		    if (!Config.AllowPaychecksInSafezone && player.Player.movement.isSafe)
			{
				ShowNotification(player, Translate("paycheck_safezone"), Color.yellow);
			    return;
		    }
		    if (Config.MinimumMovementBetweenPaychecks > 0.0f)
		    {
			    if (_playerPositions.ContainsKey(player.CSteamID))
			    {
				    var distance = (player.Position - _playerPositions[player.CSteamID]).sqrMagnitude;
					if (distance <= Mathf.Pow(Config.MinimumMovementBetweenPaychecks, 2))
					{
						ShowNotification(player, Translate("paycheck_stationary"), Color.yellow);
						return;
				    }
				}
			    _playerPositions[player.CSteamID] = player.Position;
		    }

			var experience = GetPaycheckExperienceSum(paychecks);
		    var multiplier = GetPaycheckMultiplier(player.Position, paychecks);
			float capMultiplier = Config.PaycheckXPCapModifiers.OrderBy(c => c.MinimumXP).LastOrDefault(c => c.MinimumXP < player.Experience)?.Modifier ?? 1f;

			if (capMultiplier == 0f)
            {
				ShowNotification(player, Translate("paycheck_notgiven_cap", experience), Color.yellow);
				return;
			}

			if (Mathf.Abs(multiplier) > 0.0001f)
			{
				var change = (int) (experience * multiplier * capMultiplier);
				var experienceGiven = ExperienceHelper.ChangeExperience(player, change);
				if (experienceGiven != 0)
				{
					if (capMultiplier < 1f)
						ShowNotification(player, Translate("paycheck_given_cap", experienceGiven, (capMultiplier * 100)), Color.green);
					else
						ShowNotification(player, Translate("paycheck_given", experienceGiven), Color.green);
				}
				else if (change != 0)
				{
					ShowNotification(player, Translate("paycheck_notgiven", change), Color.yellow);
				}
			}
		    else
			{
				ShowNotification(player, Translate("paycheck_zero_multiplier"), Color.yellow);
			}
	    }

		/// <summary>
		/// Sends the player a notification if DisplayNotification is true.
		/// </summary>
		/// <param name="player"></param>
		/// <param name="message"></param>
		/// <param name="color"></param>
	    public void ShowNotification(IRocketPlayer player, string message, Color color)
		{
			if (!Config.DisplayNotification) return;
			UnturnedChat.Say(player, message, color);
		}

		/// <summary>
		/// Gets the experience sum for all provided paychecks.
		/// </summary>
		/// <param name="paychecks"></param>
		/// <returns>Sum</returns>
		public int GetPaycheckExperienceSum(List<Paycheck> paychecks)
		{
			return paychecks.Sum(paycheck => paycheck.Experience);
	    }

		/// <summary>
		/// Gets all paychecks that the player has access to.
		/// </summary>
		/// <param name="player"></param>
		/// <returns>List of Paychecks</returns>
	    public List<Paycheck> GetAvailablePaychecks(UnturnedPlayer player)
	    {
			var paychecks = Config.Paychecks.Where(paycheck => 
				PermissionsHelper.HasPermission(player, "paycheck." + paycheck.Name.ToLower())).ToList();

		    if (Config.AllowMultiplePaychecks || paychecks.Count <= 1) return paychecks;

		    var highestPaycheck = paychecks[0];
		    for (var i = 1; i < paychecks.Count; i++)
		    {
			    if (paychecks[i].Experience > highestPaycheck.Experience)
			    {
				    highestPaycheck = paychecks[i];
			    }
		    }
			return new List<Paycheck> {highestPaycheck};
	    }

		/// <summary>
		/// Gets the multiplier for the provided paychecks at the provided location.
		/// </summary>
		/// <param name="position"></param>
		/// <param name="paychecks"></param>
		/// <returns>Paycheck Experience Multiplier</returns>
	    public float GetPaycheckMultiplier(Vector3 position, List<Paycheck> paychecks)
	    {
		    var zones = new List<PaycheckZone>();
		    zones.AddRange(Config.PaycheckZones);
		    foreach (var paycheck in paychecks)
		    {
			    zones.AddRange(paycheck.PaycheckZones);
		    }

			var multiplier = 1f;
		    var closestDistance = Mathf.Infinity;

		    foreach (var zone in zones)
		    {
			    if (zone.Point != null)
			    {
				    var distance = (position - zone.Point.GetValueOrDefault()).sqrMagnitude;
				    if (!(distance <= Mathf.Pow(zone.Radius, 2f))) continue;

				    if (Config.AllowMultipleMultipliers)
				    {
					    multiplier *= zone.Multiplier;
				    }
				    else if (distance < closestDistance)
				    {
					    closestDistance = distance;
					    multiplier = zone.Multiplier;
				    }
				}
				else if (zone.Node != null)
			    {
				    foreach (var node in LocationDevkitNodeSystem.Get().GetAllNodes())
				    {
					    if (!node.name.Contains(zone.Node)) continue;

					    var distance = (position - node.transform.position).sqrMagnitude;
					    if (!(distance <= Mathf.Pow(zone.Radius, 2f))) continue;

						if (Config.AllowMultipleMultipliers)
						{
							multiplier *= zone.Multiplier;
						}
					    else if (distance < closestDistance)
						{
							closestDistance = distance;
							multiplier = zone.Multiplier;
						}
					    break;
				    }
			    }
		    }
		    return multiplier;
	    }

	    public override TranslationList DefaultTranslations => new TranslationList
	    {
		    {"paycheck_zero_multiplier", "You cannot earn experience in this area!"},
		    {"paycheck_given", "You have received your paycheck of {0} experience!"},
		    {"paycheck_given_cap", "You have received your paycheck of {0} experience! Modified by {1}% because you have too much experience"},
		    {"paycheck_notgiven", "Your paycheck was {0}, but you were unable to receive it!"},
		    {"paycheck_notgiven_cap", "Your paycheck was {0}, but you were unable to receive it because you have too much experience! Spend some!"},
		    {"paycheck_dead", "You cannot receive paychecks while dead!"},
		    {"paycheck_safezone", "You cannot receive paychecks in a safezone!"},
		    {"paycheck_stationary", "You cannot receive paychecks if you haven't moved from where you were at the last payout!"},
			{"command_paycheck_not_found", "Paycheck \"{0}\" could not be found!"},
		    {"command_list_paychecks", "Current paychecks:{0}"},
		    {"command_no_paychecks", "There are no paychecks set up!"},
			{"command_default_no_zones", "There are no global zones set up!"},
		    {"command_paycheck_no_zones", "\"{0}\" has no zones set up!"},
		    {"command_list_default_zones", "Default paycheck zones:{0}"},
		    {"command_list_paycheck_zones", "Paycheck \"{0}\" paycheck zones:{1}"},
		    {"command_paycheck_deleted", "Paycheck \"{0}\" has been deleted!"},
		    {"command_delete_zone_no_parse", "Could not find zone!"},
		    {"command_invalid_out_of_bounds", "Index {0} is out of bounds {1} to {2}!"},
		    {"command_removed_zone_default", "Removed zone at {0} from global zones!"},
		    {"command_removed_zone_paycheck", "Removed zone at {1} from paycheck \"{0}\"!"},
		    {"command_no_parse_experience", "Could not parse \"{0}\" as the experience!"},
		    {"command_paycheck_created", "Created paycheck named \"{0}\" with {1}XP, players with \"paycheck.{0}\" permissions will have access to it!"},
		    {"command_no_console", "This command cannot be called from the console in this way!"},
		    {"command_no_parse_multiplier", "Could not parse \"{0}\" as the multiplier!"},
		    {"command_no_parse_radius", "Could not parse \"{0}\" as the radius!"},
		    {"command_no_parse_location", "Could not parse \"{0}\" as coordinates or a node!"},
		    {"command_created_zone_default", "Created a global zone at {0} with a multiplier of {1} and radius of {2}!"},
		    {"command_created_zone_paycheck", "Created a zone for \"{0}\" at {1} with a multiplier of {2} and radius of {3}!"},
		    {"command_no_parse_paycheck_or_location", "Could not parse \"{0}\" as a paycheck, coordinates or a node!"},
		    {"command_time_to_next_paycheck_minutes", "You will receive your next paycheck in {0} minutes, {1} seconds!"},
		    {"command_time_to_next_paycheck", "You will receive your next paycheck in {0} seconds!"}
		};
    }
}