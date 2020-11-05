﻿using System;
using System.Collections.Generic;
using System.Linq;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace Hearthstone_Deck_Tracker.Hearthstone.Secrets
{
	public class SecretsManager : SecretsEventHandler
	{
		public SecretsManager(IGame game, ArenaSettingsProvider settings, PVPDRSettingsProvider pvpdrSettings)
		{
			Game = game;
			_arenaSettings = settings;
			_pvpdrSettings = pvpdrSettings;
		}

		private readonly ArenaSettingsProvider _arenaSettings;
		private readonly PVPDRSettingsProvider _pvpdrSettings;
		protected override IGame Game { get; }
		protected override bool HasActiveSecrets => Secrets.Count > 0;

		public event Action<List<Card>> OnSecretsChanged;

		public override void Reset()
		{
			base.Reset();
			Secrets.Clear();
			OnSecretsChanged?.Invoke(new List<Card>());
		}

		public override void Refresh()
		{
			OnSecretsChanged?.Invoke(GetSecretList());
		}

		public bool NewSecret(Entity entity)
		{
			if(entity == null || !entity.IsSecret || !entity.HasTag(GameTag.CLASS))
				return false;
			if(entity.HasCardId)
				Exclude(entity.CardId, false);
			var secret = new Secret(entity);
			Secrets.Add(secret);
			OnNewSecret(secret);
			Refresh();
			Log.Info(entity.ToString());
			return true;
		}

		public bool RemoveSecret(Entity entity)
		{
			if(entity == null)
				return false;
			var secret = Secrets.FirstOrDefault(s => s.Entity.Id == entity.Id);
			if(secret != null)
			{
				HandleFastCombat(entity);
				Secrets.Remove(secret);
				if(secret.Entity.HasCardId)
				{ 
					Exclude(secret.Entity.CardId, false);
					SavedSecrets.Remove(secret.Entity.CardId);
				}
				Refresh();
				return true;
			}
			Log.Info("Secret not found: " + entity);
			return false;
		}

		public override void Exclude(List<string> cardIds)
		{
			for(var i = 0; i < cardIds.Count; i++)
				Exclude(cardIds[i], i == cardIds.Count - 1);
		}

		public override bool Exclude(string cardId, bool invokeCallback = true)
		{
			if(string.IsNullOrEmpty(cardId))
				return false;
			foreach(var secret in Secrets)
				secret.Exclude(cardId);
			Log.Info("Excluded Secret " + cardId);
			if(invokeCallback)
				Refresh();
			return true;
		}

		public void Toggle(string cardId)
		{
			var excluded = Secrets.Any(s => s.IsExcluded(cardId));
			if(excluded)
			{
				foreach(var secret in Secrets)
					secret.Include(cardId);
			}
			else
				Exclude(cardId, false);
			Refresh();
		}

		public List<Card> GetSecretList()
		{
			var gameMode = Game.CurrentGameType;
			var format = Game.CurrentFormat;

			var opponentEntities = Game.Opponent.RevealedEntities.Where(e => e.Id < 68 && e.IsSecret && e.HasCardId).ToList();
			var gameModeHasCardLimit = new[] { GameType.GT_CASUAL, GameType.GT_RANKED, GameType.GT_VS_FRIEND, GameType.GT_VS_AI }
				.Contains(gameMode);

			//List of all non-excluded cardIds for created secrets
			var createdSecrets = Secrets
				.Where(s => s.Entity.Info.Created)
				.SelectMany(s => s.Excluded)
				.Where(x => !x.Value)
				.Select(x => x.Key)
				.Distinct().ToList();

			bool HasPlayedTwoOf(string cardId) => opponentEntities.Count(e => e.CardId == cardId && !e.Info.Created) >= 2;

			int AdjustCount(string cardId, int count) => gameModeHasCardLimit && HasPlayedTwoOf(cardId)
														&& !createdSecrets.Contains(cardId) ? 0 : count;

			var cards = Secrets.SelectMany(secret => secret.Excluded).GroupBy(id => id.Key).Select(group =>
			{
				var card = Database.GetCardFromId(group.Key);
				card.Count = AdjustCount(group.Key, group.Count(x => !x.Value));
				return card;
			});

			if(gameMode == GameType.GT_ARENA)
			{
				cards = cards.Where(c => _arenaSettings.CurrentSets.Contains(c.CardSet ?? CardSet.BLANK));
				if (_arenaSettings.BannedSecrets.Count > 0)
					cards = cards.Where(c => !_arenaSettings.BannedSecrets.Contains(c.Id));
			}
			else
			{
				if(_arenaSettings.ExclusiveSecrets.Count > 0)
					cards = cards.Where(c => !_arenaSettings.ExclusiveSecrets.Contains(c.Id));
				if(format == Format.Standard)
					cards = cards.Where(c => !Helper.WildOnlySets.Contains(c.Set));
			}

			if(gameMode == GameType.GT_PVPDR || gameMode == GameType.GT_PVPDR_PAID)
			{
				cards = cards.Where(c => _pvpdrSettings.CurrentSets.Contains(c.CardSet ?? CardSet.BLANK));
				if(_pvpdrSettings.BannedSecrets.Count > 0)
					cards = cards.Where(c => !_pvpdrSettings.BannedSecrets.Contains(c.Id));
			}

			return cards.ToList();
		}
	}
}
