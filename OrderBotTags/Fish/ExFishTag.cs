namespace ExBuddy.OrderBotTags.Fish
{
	using Buddy.Coroutines;
	using Clio.Common;
	using Clio.Utilities;
	using Clio.XmlEngine;
	using ExBuddy.Attributes;
	using ExBuddy.Enumerations;
	using ExBuddy.Helpers;
	using ExBuddy.OrderBotTags.Behaviors;
	using ExBuddy.OrderBotTags.Objects;
	using ff14bot;
	using ff14bot.Behavior;
	using ff14bot.Enums;
	using ff14bot.Managers;
	using ff14bot.Navigation;
	using ff14bot.Objects;
	using ff14bot.Pathing;
	using ff14bot.RemoteWindows;
	using ff14bot.Settings;
	using System;
	using System.Collections.Generic;
	using System.ComponentModel;
	using System.Globalization;
	using System.Linq;
	using System.Text.RegularExpressions;
	using System.Threading.Tasks;
	using System.Windows.Media;
	using TreeSharp;
	using Action = TreeSharp.Action;

	[LoggerName("ExFish")]
	[XmlElement("ExFish")]
	[XmlElement("Fish")]
	public class ExFishTag : ExProfileBehavior
	{

		private readonly Windows.Bait baitWindow = new Windows.Bait();

		internal SpellData CordialSpellData;

		protected override Color Info => Colors.Gold;

		public static bool IsFishing()
		{
			return isFishing;
		}

		protected override Composite CreateBehavior()
		{
			fishlimit = GetFishLimit();

			return new PrioritySelector(
				StateTransitionAlwaysSucceed,
				Conditional,
				Blacklist,
				MoveToFishSpot,
				GoFish(
					StopMovingComposite,
					DismountComposite,
					CheckStealthComposite,
					CheckWeatherComposite,
					// Waits up to 10 hours, might want to rethink this one.
					new ExCoroutineAction(ctx => HandleBait(), this),
					InitFishSpotComposite,
					new ExCoroutineAction(ctx => HandleCollectable(), this),
					ReleaseComposite,
					IdenticalCastComposite,
					MoochComposite,
					SurfaceSlapComposite,
					FishCountLimitComposite,
					InventoryFullComposite,
					SitComposite,
					ThaliaksFavorComposite,
					CollectorsGloveComposite,
					SnaggingComposite,
					new ExCoroutineAction(ctx => HandleCordial(), this),
					PrizeCatchComposite,
					PatienceComposite,
					MakeshiftBaitComposite,
					FishEyesComposite,
					ChumComposite,
					CastComposite,
					HookComposite));
		}

		protected virtual void DoCleanup()
		{
			try
			{
				GamelogManager.MessageRecevied -= ReceiveMessage;
			}
			catch (Exception ex)
			{
				Logger.Error(ex.Message);
			}

			isFishing = false;

			CharacterSettings.Instance.UseMount = initialMountSetting;
		}

		protected override void DoReset()
		{
			mooch = 0;
			sitRoll = 1.0;
			spotinit = false;
			fishcount = 0;
			amissfish = 0;
			isFishing = false;
			isSitting = false;
			isFishIdentified = false;
			fishlimit = GetFishLimit();
			checkRelease = false;
			checkIdenticalCast = false;

			// Temp fix, only set it to true if it was initially true. Need to find out why this value is false here when it shouldn't be.
			if (initialMountSetting)
			{
				CharacterSettings.Instance.UseMount = initialMountSetting;
			}
		}

		protected Composite GoFish(params Composite[] children)
		{
			return
				new PrioritySelector(
					new Decorator(
						ret => Vector3.Distance(ExProfileBehavior.Me.Location, FishSpots.CurrentOrDefault.Location) < 2,
						new PrioritySelector(children)));
		}

		protected override Task<bool> Main()
		{
			throw new NotImplementedException();
		}

		protected override void OnDone()
		{
			TreeRoot.OnStop -= cleanup;
			DoCleanup();
		}

		protected override void OnStart()
		{
			BaitDelay = BaitDelay.Clamp(0, 5000);

			Item baitItem = null;
			if (BaitId > 0)
			{
				baitItem = DataManager.ItemCache[BaitId];
			}
			else if (!string.IsNullOrWhiteSpace(Bait))
			{
				baitItem =
					DataManager.ItemCache.Values.Find(
						i =>
							string.Equals(i.EnglishName, Bait, StringComparison.InvariantCultureIgnoreCase)
							|| string.Equals(i.CurrentLocaleName, Bait, StringComparison.InvariantCultureIgnoreCase));

				if (baitItem == null)
				{
					isDone = true;
					Logger.Error(Localization.Localization.ExFish_CannotFindBait + Bait);
					return;
				}
			}

			if (baitItem != null)
			{
				if (Baits == null)
				{
					Baits = new List<Bait>();
				}

				Baits.Insert(0, new Bait { Id = baitItem.Id, Name = baitItem.EnglishName, BaitItem = baitItem, Condition = "True" });
			}

			if (baitItem != null && baitItem.Affinity != 19)
			{
				isDone = true;
				Logger.Error(Localization.Localization.ExFish_IsNotBait, baitItem.EnglishName);
				return;
			}

			if (Keepers == null)
			{
				Keepers = new List<Keeper>();
			}

			if (SurfaceSlaps == null)
			{
				SurfaceSlaps = new List<SurfaceSlap>();
			}

			if (ReverseHooks == null)
			{
				ReverseHooks = new List<ReverseHook>();
			}

			if (DoubleHooks == null)
			{
				DoubleHooks = new List<DoubleHook>();
			}

			if (TripleHooks == null)
			{
				TripleHooks = new List<TripleHook>();
			}

			if (Collect && Collectables == null)
			{
				Collectables = new List<Collectable> { new Collectable { Name = string.Empty, Value = (int)CollectabilityValue } };
			}

			GamelogManager.MessageRecevied += ReceiveMessage;
			FishSpots.IsCyclic = true;
			isFishing = false;
			isSitting = false;
			initialMountSetting = CharacterSettings.Instance.UseMount;
			ShuffleFishSpots();

			sitRoll = SitRng.NextDouble();

			if (CanDoAbility(Ability.Quit))
			{
				DoAbility(Ability.Quit);
			}

			CordialSpellData = DataManager.GetItem((uint)CordialType.Cordial).BackingAction;

			cleanup = bot =>
			{
				DoCleanup();
				TreeRoot.OnStop -= cleanup;
			};

			TreeRoot.OnStop += cleanup;
		}

		internal bool CanUseCordial(ushort withinSeconds = 5)
		{
			return CordialSpellData.Cooldown.TotalSeconds < withinSeconds && !HasChum && !HasPatience && !HasFishEyes
				   && ((CordialType == CordialType.WateredCordial && Cordial.HasWateredCordials())
				   || (CordialType == CordialType.Cordial && (Cordial.HasWateredCordials() || Cordial.HasCordials()))
				   || ((CordialType == CordialType.HiCordial || CordialType == CordialType.Auto) && Cordial.HasAnyCordials()));
		}

		private async Task<bool> HandleBait()
		{
			if (!IsBaitSpecified || IsCorrectBaitSelected)
			{
				// we don't need to worry about bait. Either not specified, or we already have the correct bait selected.
				return false;
			}

			if (FishingManager.State != FishingState.None && FishingManager.State != FishingState.PoleReady)
			{
				// we are not in the proper state to modify our bait. continue.
				return false;
			}

			if (!HasSpecifiedBait)
			{
				Logger.Error(Localization.Localization.ExFish_NotHaveBait + Bait);
				return isDone = true;
			}

			var baitItem = Fish.Bait.FindMatch(Baits).BaitItem;

			if (!await baitWindow.SelectBait(baitItem.Id, (ushort)BaitDelay))
			{
				Logger.Error(Localization.Localization.ExFish_BaitSelectError);
				return isDone = true;
			}

			Logger.Info(Localization.Localization.ExFish_UseBait + baitItem.EnglishName);

			return true;
		}

		private async Task<bool> HandleCollectable()
		{
			if (Collectables == null)
			{
				//we are not collecting
				return false;
			}

			if (FishingManager.State != FishingState.Waitin)
			{
				// we are not waitin yet!
				return false;
			}

			if (!SelectYesNoItem.IsOpen)
			{
				//Wait a few seconds
				var opened = await Coroutine.Wait(5000, () => SelectYesNoItem.IsOpen);
				if (!opened)
				{
					Logger.Info("SelectYesNoItem never appeared");
					return false;
				}
			}

			var required = CollectabilityValue;
			var itemName = string.Empty;
			if (!string.IsNullOrWhiteSpace(Collectables.First().Name))
			{
				var item = SelectYesNoItem.Item;
				if (item == null
					|| !Collectables.Any(c => string.Equals(c.Name, item.EnglishName, StringComparison.InvariantCultureIgnoreCase)))
				{
					var ticks = 0;
					while ((item == null
							||
							!Collectables.Any(c => string.Equals(c.Name, item.EnglishName, StringComparison.InvariantCultureIgnoreCase)))
						   && ticks++ < 60 && Behaviors.ShouldContinue)
					{
						item = SelectYesNoItem.Item;
						await Coroutine.Yield();
					}

					// handle timeout
					if (ticks > 60)
					{
						required = (uint)Collectables.Select(c => c.Value).Max();
					}
				}

				if (item != null)
				{
					// handle normal
					itemName = item.EnglishName;
					var collectable = Collectables.FirstOrDefault(c => string.Equals(c.Name, item.EnglishName));

					if (collectable != null)
					{
						required = (uint)collectable.Value;
					}
				}
			}

			// handle

			var value = SelectYesNoItem.CollectabilityValue;

			if (value >= required)
			{
				Logger.Info(Localization.Localization.ExFish_Collecting, itemName, value, required);
				SelectYesNoItem.Yes();
			}
			else
			{
				Logger.Info(Localization.Localization.ExFish_Declining, itemName, value, required);
				SelectYesNoItem.No();
			}

			await Coroutine.Wait(3000, () => !SelectYesNoItem.IsOpen && FishingManager.State != FishingState.Waitin);

			return true;
		}

		private async Task<bool> HandleCordial()
		{
			if (CordialType == CordialType.None)
			{
				// Not using cordials, skip method.
				return false;
			}

			if (FishingManager.State >= FishingState.Bite)
			{
				// Need to wait till we are in the correct state
				return false;
			}

			CordialSpellData = CordialSpellData ?? Cordial.GetSpellData();

			if (CordialSpellData == null)
			{
				CordialType = CordialType.None;
				return false;
			}

			if (!CanUseCordial(8))
			{
				// has a buff or cordial cooldown not ready or we have no cordials.
				return false;
			}

			var missingGp = ExProfileBehavior.Me.MaxGP - ExProfileBehavior.Me.CurrentGP;

			if (missingGp < 300 && !ForceCordial)
			{
				// Not forcing cordial and less than 300gp missing from max.
				return false;
			}

			await Coroutine.Wait(5000, () => FishingManager.State == FishingState.None);

			if (missingGp >= 430 && (CordialType == CordialType.HiCordial || CordialType == CordialType.Auto) && Cordial.HasHiCordials())
			{
				if (await UseCordial(CordialType.HiCordial))
				{
					return true;
				}
			}

			if (missingGp >= 380 && (CordialType == CordialType.Cordial || CordialType == CordialType.Auto) && Cordial.HasCordials())
			{
				if (await UseCordial(CordialType.Cordial))
				{
					return true;
				}
			}

			if (missingGp >= 230 && (CordialType == CordialType.WateredCordial || CordialType == CordialType.Auto) && Cordial.HasWateredCordials())
			{
				if (await UseCordial(CordialType.WateredCordial))
				{
					return true;
				}
			}

			return false;
		}

		private async Task<bool> UseCordial(CordialType cordialType, int maxTimeoutSeconds = 5)
		{
			if (CordialSpellData.Cooldown.TotalSeconds < maxTimeoutSeconds)
			{
				var cordial = InventoryManager.FilledSlots.FirstOrDefault(slot => slot.RawItemId == (uint)cordialType);

				if (cordial != null)
				{
					StatusText = Localization.Localization.ExFish_UseCordialWhenAvailable;

					Logger.Info(
						Localization.Localization.ExFish_UseCordial,
						(int)CordialSpellData.Cooldown.TotalSeconds,
						ExProfileBehavior.Me.CurrentGP);

					if (await Coroutine.Wait(
						TimeSpan.FromSeconds(maxTimeoutSeconds),
						() =>
						{
							if (ExProfileBehavior.Me.IsMounted && CordialSpellData.Cooldown.TotalSeconds < 2)
							{
								ActionManager.Dismount();
								return false;
							}

							return cordial.CanUse(ExProfileBehavior.Me);
						}))
					{
						await Coroutine.Sleep(500);
						Logger.Info("Using " + cordialType);
						cordial.UseItem(ExProfileBehavior.Me);
						await Coroutine.Sleep(1500);
						return true;
					}
				}
				else
				{
					Logger.Warn(Localization.Localization.ExFish_NoCordial + cordialType);
				}
			}

			return false;
		}

		#region Aura Properties

		protected bool HasPatience
		{
			get
			{
				// Gathering Fortune Up (Fishing)
				return ExProfileBehavior.Me.HasAura(850);
			}
		}

		protected bool HasSnagging
		{
			get
			{
				// Snagging
				return ExProfileBehavior.Me.HasAura(761);
			}
		}

		protected bool HasCollectorsGlove
		{
			get
			{
				// Collector's Glove
				return ExProfileBehavior.Me.HasAura(805);
			}
		}

		protected bool HasChum
		{
			get
			{
				// Chum
				return ExProfileBehavior.Me.HasAura(763);
			}
		}

		protected bool HasFishEyes
		{
			get
			{
				// Fish Eyes
				return ExProfileBehavior.Me.HasAura(762);
			}
		}

		protected bool HasMakeshiftBait
		{
			get
			{
				// Makeshift Bait
				return ExProfileBehavior.Me.HasAura(2779);
			}
		}

		protected bool HasPrizeCatch
		{
			get
			{
				// Gathering Prize Catch
				return ExProfileBehavior.Me.HasAura(2780);
			}
		}

		protected bool HasAnglersArt
		{
			get
			{
				// Angler's Art
				return ExProfileBehavior.Me.HasAura(2778);
			}
		}

		protected int GetAnglersArtStack
		{
			get{
				// Angler's Art Stack Count
				return Core.Player.Auras.GetAuraStacksById(2778);
			}
		}

		#endregion Aura Properties

		#region Fields

		private static bool isFishing;

		protected static readonly Random SitRng = new Random();

		protected static Regex FishRegex = new Regex(

#if RB_CN
            @"[\u4e00-\u9fa5A-Za-z0-9·]+成功钓上了|[\u4e00-\u9fa5]+|\ue03c",
#else
			@"You land(?: a| an)? (.+) measuring (\d{1,4}\.\d) ilms!",
#endif
			RegexOptions.Compiled | RegexOptions.IgnoreCase);

		protected static Regex FishSizeRegex = new Regex(@"(\d{1,4}\.\d)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		protected static FishResult FishResult = new FishResult();

		private Func<bool> conditionFunc;

		private Func<bool> moochConditionFunc;

		private bool initialMountSetting;

		private BotEvent cleanup;

		private bool checkRelease;

		private bool checkIdenticalCast;

		private bool isSitting;

		private bool isFishIdentified;

		private int mooch;

		private int fishcount;

		private int amissfish;

		private double fishlimit;

		private double sitRoll = 1.0;

		private bool spotinit;

		#endregion Fields

		#region Public Properties

		[DefaultValue("True")]
		[XmlAttribute("Condition")]
		public string Condition { get; set; }

		[XmlAttribute("Weather")]
		public string Weather { get; set; }

		[XmlAttribute("Stealth")]
		public bool Stealth { get; set; }

		[DefaultValue(2.0f)]
		[XmlAttribute("Radius")]
		public float Radius { get; set; }

		[DefaultValue(CordialType.None)]
		[XmlAttribute("CordialType")]
		public CordialType CordialType { get; set; }

		[XmlAttribute("ForceCordial")]
		public bool ForceCordial { get; set; }

		[XmlElement("Baits")]
		public List<Bait> Baits { get; set; }

		[XmlAttribute("Bait")]
		public string Bait { get; set; }

		[XmlAttribute("BaitId")]
		public uint BaitId { get; set; }

		[DefaultValue(200)]
		[XmlAttribute("BaitDelay")]
		public int BaitDelay { get; set; }

		[DefaultValue(20)]
		[XmlAttribute("MinFish")]
		public int MinimumFishPerSpot { get; set; }

		[DefaultValue(30)]
		[XmlAttribute("MaxFish")]
		public int MaximumFishPerSpot { get; set; }

		[XmlElement("FishSpots")]
		public IndexedList<FishSpot> FishSpots { get; set; }

		[XmlAttribute("ShuffleFishSpots")]
		public bool Shuffle { get; set; }

		[DefaultValue(30)]
		[XmlAttribute("LastFishTimeout")]
		public int LastFishTimeout { get; set; }

		[XmlElement("Keepers")]
		public List<Keeper> Keepers { get; set; }

		[DefaultValue(true)]
		[XmlAttribute("EnableKeeper")]
		public bool EnableKeeper { get; set; }

		[DefaultValue(false)]
		[XmlAttribute("KeepNone")]
		public bool KeepNone { get; set; }

		[XmlAttribute("Sit")]
		public bool Sit { get; set; }

		[XmlAttribute("SitRate")]
		public float SitRate { get; set; }

		[XmlElement("Collectables")]
		public List<Collectable> Collectables { get; set; }

		[XmlAttribute("Collect")]
		public bool Collect { get; set; }

		[XmlAttribute("CollectabilityValue")]
		public uint CollectabilityValue { get; set; }

		[DefaultValue(0)]
		[XmlAttribute("Mooch")]
		public int MoochLevel { get; set; }

		[DefaultValue(false)]
		[XmlAttribute("UseMooch2")]
		public bool UseMooch2 { get; set; }

		[DefaultValue("True")]
		[XmlAttribute("MoochCondition")]
		public string MoochCondition { get; set; }

		[DefaultValue(false)]
		[XmlAttribute("MakeshiftBait")]
		public bool MakeshiftBait { get; set; }

		[DefaultValue(Ability.None)]
		[XmlAttribute("Patience")]
		internal Ability Patience { get; set; }

		[DefaultValue(600)]
		[XmlAttribute("MinimumGPPatience")]
		public int MinimumGPPatience { get; set; }

		[XmlElement("PatienceTugs")]
		public List<PatienceTug> PatienceTugs { get; set; }

		[XmlAttribute("PrizeCatch")]
		public bool PrizeCatch { get; set; }

		[DefaultValue(600)]
		[XmlAttribute("MinimumGPPrizeCatch")]
		public int MinimumGPPrizeCatch { get; set; }

		[XmlAttribute("Chum")]
		public bool Chum { get; set; }

		[XmlAttribute("FishEyes")]
		public bool FishEyes { get; set; }

		[XmlAttribute("Snagging")]
		public bool Snagging { get; set; }

		[XmlAttribute("IdenticalCast")]
		public bool IdenticalCast { get; set; }

		[DefaultValue(false)]
		[XmlAttribute("ThaliaksFavor")]
		public bool ThaliaksFavor { get; set; }

		[DefaultValue(600)]
		[XmlAttribute("MaximumGPThaliaksFavor")]
		public int MaximumGPThaliaksFavor { get; set; }

		[DefaultValue(3)]
		[XmlAttribute("ThaliaksFavorThreshold")]
		public int ThaliaksFavorThreshold { get; set; }

		[XmlElement("SurfaceSlaps")]
		public List<SurfaceSlap> SurfaceSlaps { get; set; }

		[DefaultValue(true)]
		[XmlAttribute("EnableSurfaceSlap")]
		public bool EnableSurfaceSlap { get; set; }

		[DefaultValue(300)]
		[XmlAttribute("MinimumGPSurfaceSlap")]
		public int MinimumGPSurfaceSlap { get; set; }

		[XmlElement("DoubleHooks")]
		public List<DoubleHook> DoubleHooks { get; set; }

		[XmlElement("TripleHooks")]
		public List<TripleHook> TripleHooks { get; set; }

		[XmlElement("ReverseHooks")]
		public List<ReverseHook> ReverseHooks { get; set; }

		#endregion Public Properties

		#region Private Properties

		internal bool MovementStopCallback(float distance, float radius)
		{
			return distance <= radius || !ConditionCheck() || ExProfileBehavior.Me.IsDead;
		}

		private bool HasSpecifiedBait
		{
			get { return Fish.Bait.FindMatch(Baits).BaitItem.ItemCount() > 0; }
		}

		private bool IsBaitSpecified
		{
			get { return Baits != null && Baits.Count > 0; }
		}

		private bool IsCorrectBaitSelected
		{
			get { return Fish.Bait.FindMatch(Baits).BaitItem.Id == FishingManager.SelectedBaitItemId; }
		}

		#endregion Private Properties

		#region Fishing Composites

		protected Composite DismountComposite
		{
			get { return new Decorator(ret => ExProfileBehavior.Me.IsMounted, CommonBehaviors.Dismount()); }
		}

		protected Composite FishCountLimitComposite
		{
			get
			{
				return
					new Decorator(
						ret =>
							fishcount >= fishlimit && !HasPatience && CanDoAbility(Ability.Quit)
							&& FishingManager.State == FishingState.PoleReady && !SelectYesNoItem.IsOpen,
						new Sequence(
							new Sleep(2, 3),
							new Action(r => { DoAbility(Ability.Quit); }),
							new Sleep(2, 3),
							new Action(r => { ChangeFishSpot(); })));
			}
		}

		protected Composite SitComposite
		{
			get
			{
				return
					new Decorator(
						ret =>
							!isSitting && (Sit || FishSpots.CurrentOrDefault.Sit || sitRoll < SitRate)
							&& FishingManager.State == (FishingState)9,
						// this is when you have already cast and are waiting for a bite.
						new Sequence(
							new Sleep(1, 1),
							new Action(
								r =>
								{
									isSitting = true;
									Logger.Info(Localization.Localization.ExFish_Sit + FishSpots.CurrentOrDefault);
									ChatManager.SendChat("/sit");
								})));
			}
		}

		protected Composite StopMovingComposite
		{
			get { return new Decorator(ret => MovementManager.IsMoving, CommonBehaviors.MoveStop()); }
		}

		protected Composite InitFishSpotComposite
		{
			get
			{
				return new Decorator(
					ret => !spotinit,
					new Action(
						r =>
						{
							FaceFishSpot();
							isFishing = true;
							Logger.Info(Localization.Localization.ExFish_InitFishSpot + fishlimit + Localization.Localization.ExFish_InitFishSpot2);
							spotinit = true;
						}));
			}
		}

		protected Composite CheckWeatherComposite
		{
			get
			{
				return new Decorator(
					ret => Weather != null && Weather != WorldManager.CurrentWeather,
					new Sequence(
						new Action(r => { Logger.Info(Localization.Localization.ExFish_CheckWeather); }),
						new Wait(36000, ret => Weather == WorldManager.CurrentWeather, new ActionAlwaysSucceed())));
			}
		}

		protected Composite CollectorsGloveComposite
		{
			get
			{
				return new Decorator(
					ret => CanDoAbility(Ability.CollectorsGlove) && Collectables != null ^ HasCollectorsGlove,
					new Sequence(
						new Action(
							r =>
							{
								Logger.Info(Localization.Localization.ExFish_CollectorsGlove);
								DoAbility(Ability.CollectorsGlove);
							}),
						new Sleep(2, 3)));
			}
		}

		protected Composite SnaggingComposite
		{
			get
			{
				return new Decorator(
					ret => CanDoAbility(Ability.Snagging) && Snagging ^ HasSnagging,
					new Sequence(
						new Action(
							r =>
							{
								Logger.Info(Localization.Localization.ExFish_Snagging);
								DoAbility(Ability.Snagging);
							}),
						new Sleep(2, 3)));
			}
		}

		protected Composite MoochComposite
		{
			get
			{
				return
					new Decorator(
						ret =>
							(CanDoAbility(Ability.Mooch) || CanDoAbility(Ability.Mooch2)) && MoochConditionCheck() && (MoochLevel != 0 && mooch < MoochLevel) 
							&& (!EnableKeeper || Keepers.Count == 0
								|| Keepers.All(k => !string.Equals(k.Name, FishResult.FishName, StringComparison.InvariantCultureIgnoreCase))
								|| Keepers.Any(k => string.Equals(k.Name, FishResult.FishName, StringComparison.InvariantCultureIgnoreCase)
								&& FishResult.ShouldMooch(k))),
						new Sequence(
							new Action(
								r =>
								{
									checkRelease = true;
									var moochAbility = UseMooch2 && CanDoAbility(Ability.Mooch2) ? Ability.Mooch2 : Ability.Mooch;
									DoAbility(moochAbility);
									mooch++;
									if (MoochLevel > 1)
									{
										Logger.Info(Localization.Localization.ExFish_Mooch, moochAbility.ToString(), mooch, MoochLevel);
									}
									else
									{
										Logger.Info(Localization.Localization.ExFish_Mooch2, moochAbility.ToString());
									}
								}),
							new Sleep(2, 2)));
			}
		}

		protected Composite ChumComposite
		{
			get
			{
				return new Decorator(
					ret => (FishingManager.State == FishingState.None || FishingManager.State == FishingState.PoleReady)
							&& Chum && !HasChum && CanDoAbility(Ability.Chum),
					new Sequence(new Action(r => 
					{
						DoAbility(Ability.Chum);
						Logger.Info(Localization.Localization.ExFish_Chum);
					}
					), new Sleep(1, 2)));
			}
		}

		protected Composite MakeshiftBaitComposite
		{
			get
			{
				return
					new Decorator(
						ret => !HasMakeshiftBait && !HasPatience && MakeshiftBait && GetAnglersArtStack >= 5 && CanDoAbility(Ability.MakeshiftBait)
							   && (FishingManager.State == FishingState.None || FishingManager.State == FishingState.PoleReady),
						new Sequence(new Action(r => 
						{
							DoAbility(Ability.MakeshiftBait);
							Logger.Info(Localization.Localization.ExFish_MakeshiftBait);
						}
						), new Sleep(1, 2)));
			}
		}

		protected Composite PatienceComposite
		{
			get
			{
				return
					new Decorator(
						ret =>
							Patience > Ability.None && (FishingManager.State == FishingState.None || FishingManager.State == FishingState.PoleReady) && !HasPatience && !HasMakeshiftBait
							&& CanDoAbility(Patience) && (ExProfileBehavior.Me.CurrentGP >= MinimumGPPatience || ExProfileBehavior.Me.CurrentGPPercent > 99.0f),
						new Sequence(
							new Action(
								r =>
								{
									DoAbility(Patience);
									Logger.Info(Localization.Localization.ExFish_Patience);
								}),
							new Sleep(1, 2)));
			}
		}

		protected Composite ThaliaksFavorComposite
		{
			get
			{
				return
					new Decorator(
						ret => ThaliaksFavor && GetAnglersArtStack >= ThaliaksFavorThreshold && CanDoAbility(Ability.ThaliaksFavor)
							   && (ExProfileBehavior.Me.CurrentGP <= MaximumGPThaliaksFavor || (ExProfileBehavior.Me.MaxGP - ExProfileBehavior.Me.CurrentGP) > 200)
							   && (FishingManager.State == FishingState.None || FishingManager.State == FishingState.PoleReady),
						new Sequence(new Action(r => 
						{
							DoAbility(Ability.ThaliaksFavor);
							Logger.Info(Localization.Localization.ExFish_ThaliaksFavor, GetAnglersArtStack - 3);
						}
						), new Sleep(1, 2)));
			}
		}

		protected Composite PrizeCatchComposite
		{
			get
			{
				return
					new Decorator(
						ret => PrizeCatch && !HasPrizeCatch && ExProfileBehavior.Me.CurrentGP <= MinimumGPPrizeCatch && CanDoAbility(Ability.PrizeCatch),
						new Sequence(new Action(r => 
						{
							DoAbility(Ability.PrizeCatch);
							Logger.Info(Localization.Localization.ExFish_PrizeCatch);
						}
						), new Sleep(1, 2)));
			}
		}

		protected Composite FishEyesComposite
		{
			get
			{
				return new Decorator(
					ret => FishEyes && !HasFishEyes && CanDoAbility(Ability.FishEyes),
					new Sequence(new Action(r =>
					{
						DoAbility(Ability.FishEyes);
						Logger.Info(Localization.Localization.ExFish_FishEyes);
					}
					), new Sleep(1, 2)));
			}
		}

		protected Composite IdenticalCastComposite
		{
			get
			{
				return
					new Decorator(
						ret =>
							IdenticalCast && checkIdenticalCast && FishingManager.State == FishingState.PoleReady && CanDoAbility(Ability.IdenticalCast)
							&& (Keepers.Count != 0 || KeepNone),
						new Sequence(
							new Wait(
								2,
								ret => isFishIdentified,
								new Action(
									r =>
									{
										// If its a keeper AND (we aren't mooching OR we can't mooch) AND Keeper is enabled, then use Identical Cast
										if (Keepers.Any(FishResult.IsKeeper) && (MoochLevel == 0 || !CanDoAbility(Ability.Mooch)) && EnableKeeper)
										{
											DoAbility(Ability.IdenticalCast);
											Logger.Info(Localization.Localization.ExFish_IdenticalCast, FishResult.Name);
										}

										checkIdenticalCast = false;
									})),
							new Wait(2, ret => !CanDoAbility(Ability.Release), new ActionAlwaysSucceed())));
			}
		}

		protected Composite SurfaceSlapComposite
		{
			get
			{
				return
					new Decorator(
						ret => 
							EnableSurfaceSlap && FishingManager.State == FishingState.PoleReady && CanDoAbility(Ability.SurfaceSlap)
							&& (ExProfileBehavior.Me.CurrentGP >= MinimumGPSurfaceSlap || ExProfileBehavior.Me.CurrentGPPercent > 99.0f)
							&& (MoochLevel == 0 || !CanDoAbility(Ability.Mooch))
							&& SurfaceSlaps.Any(s => string.Equals(s.Name, FishResult.FishName, StringComparison.InvariantCultureIgnoreCase)),
						new Sequence(
							new Action(
								r =>
								{
									DoAbility(Ability.SurfaceSlap);
									Logger.Info(Localization.Localization.ExFish_SurfaceSlap, FishResult.FishName);
								}),
							new Sleep(2, 3)));
			}
		}

		protected Composite ReleaseComposite
		{
			get
			{
				return
					new Decorator(
						ret =>
							checkRelease && FishingManager.State == FishingState.PoleReady && CanDoAbility(Ability.Release)
							&& (Keepers.Count != 0 || KeepNone),
						new Sequence(
							new Wait(
								2,
								ret => isFishIdentified,
								new Action(
									r =>
									{
										// If its not a keeper AND (we aren't mooching OR we can't mooch) AND Keeper is enabled, then release
										if (!Keepers.Any(FishResult.IsKeeper) && (MoochLevel == 0 || !CanDoAbility(Ability.Mooch)) && !CanDoAbility(Ability.Mooch2) && EnableKeeper)
										{
											DoAbility(Ability.Release);
											Logger.Info(Localization.Localization.ExFish_Release, FishResult.Name);
										}

										checkRelease = false;
									})),
							new Wait(2, ret => !CanDoAbility(Ability.Release), new ActionAlwaysSucceed())));
			}
		}

		protected Composite CastComposite
		{
			get
			{
				return
					new Decorator(
						ret => FishingManager.State == FishingState.None || FishingManager.State == FishingState.PoleReady,
						new Action(r => Cast()));
			}
		}

		protected Composite InventoryFullComposite
		{
			get
			{
				return new Decorator(
					// TODO: Log reason for quit.
					ret => InventoryManager.FilledSlots.Count(c => c.BagId != InventoryBagId.KeyItems) >= 140, IsDoneAction);
			}
		}

		protected Composite HookComposite
		{
			get
			{
				return new Decorator(
					ret => FishingManager.CanHook && FishingManager.State == FishingState.Bite,
					new Action(
						r =>
						{
							var tugType = FishingManager.TugType;
							var patienceTug = new PatienceTug { MoochLevel = mooch, TugType = tugType };
							var hookset = tugType == TugType.Light ? Ability.PrecisionHookset : Ability.PowerfulHookset;
							if (HasPatience && ReverseHooks.Any(r => (r.MoochLevel == mooch) && (string.Equals(r.TugType, tugType.ToString(), StringComparison.InvariantCultureIgnoreCase))))
							{
								hookset = tugType == TugType.Heavy ? Ability.PrecisionHookset : Ability.PowerfulHookset;
								Logger.Info(Localization.Localization.ExFish_TugType, tugType, hookset);
							}
							if (HasPatience && CanDoAbility(hookset) && (PatienceTugs == null || PatienceTugs.Contains(patienceTug)))
							{
								DoAbility(hookset);
								Logger.Info(Localization.Localization.ExFish_TugType, tugType, hookset);
							}
							else if (!HasPatience && CanDoAbility(Ability.TripleHook) && TripleHooks.Any(s => string.Equals(s.TugType, tugType.ToString(), StringComparison.InvariantCultureIgnoreCase)))
							{
								DoAbility(Ability.TripleHook);
								Logger.Info(Localization.Localization.ExFish_Hook, "Triple Hook", tugType);
							}
							else if (!HasPatience && CanDoAbility(Ability.DoubleHook) && DoubleHooks.Any(s => string.Equals(s.TugType, tugType.ToString(), StringComparison.InvariantCultureIgnoreCase)))
							{
								DoAbility(Ability.DoubleHook);
								Logger.Info(Localization.Localization.ExFish_Hook, "Double Hook", tugType);
							}
							else
							{
								FishingManager.Hook();
								Logger.Info(Localization.Localization.ExFish_Hook, "Hook", tugType);
							}

							amissfish = 0;
							if (mooch == 0)
							{
								fishcount++;
							}

							Logger.Info(Localization.Localization.ExFish_Fish, fishcount, fishlimit);
						}));
			}
		}

		protected Composite CheckStealthComposite
		{
			get
			{
				return new Decorator(
					ret => Stealth && !ExProfileBehavior.Me.HasAura(47),
					new Sequence(
						new Action(
							r =>
							{
								CharacterSettings.Instance.UseMount = false;
								DoAbility(Ability.Sneak);
							}),
						new Sleep(2, 3)));
			}
		}

		#endregion Fishing Composites

		#region Composites

		protected Composite Conditional
		{
			get { return new Decorator(ret => FishingManager.State < FishingState.Bite && !ConditionCheck(), IsDoneAction); }
		}

		protected Composite Blacklist
		{
			get
			{
				return new Decorator(
					ret => amissfish > Math.Min(FishSpots.Count, 4),
					new Sequence(
						new Action(
							r =>
							{
								Logger.Warn(Localization.Localization.ExFish_AmissFish);
								Logger.Warn(Localization.Localization.ExFish_BlackList);
							}),
						IsDoneAction));
			}
		}

		protected Composite StateTransitionAlwaysSucceed
		{
			get
			{
				return
					new Decorator(
						ret =>
							FishingManager.State == FishingState.Reelin || FishingManager.State == FishingState.Quit
							|| FishingManager.State == FishingState.PullPoleIn,
						new ActionAlwaysSucceed());
			}
		}

		protected Composite MoveToFishSpot
		{
			get
			{
				return new Decorator(
					ret => Vector3.Distance(ExProfileBehavior.Me.Location, FishSpots.CurrentOrDefault.Location) > 1,
					new Sequence(
						new Action(r =>
							{
								if (!MovementManager.IsFlying && !MovementManager.IsDiving)
								{
									Navigator.MoveTo(new MoveToParameters(FishSpots.CurrentOrDefault.Location));
								}
								else
								{
									Flightor.MoveTo(new FlyToParameters(FishSpots.CurrentOrDefault.Location));
								}
							})));
			}
		}

		protected Composite IsDoneAction
		{
			get
			{
				return
					new Sequence(
						new WaitContinue(
							LastFishTimeout,
							ret => FishingManager.State < FishingState.Bite,
							new Sequence(
								new PrioritySelector(
									new ExCoroutineAction(ctx => HandleCollectable(), this),
									ReleaseComposite,
									new ActionAlwaysSucceed()),
								new Sleep(2, 3),
								new Action(r => DoAbility(Ability.Quit)),
								new Sleep(2, 3),
								new Action(r => { isDone = true; }))));
			}
		}

		#endregion Composites

		#region Ability Checks and Actions

		internal bool CanDoAbility(Ability ability)
		{
			return ActionManager.CanCast(Abilities.Map[ClassJobType.Fisher][ability], ExProfileBehavior.Me);
		}

		internal bool DoAbility(Ability ability)
		{
			return ActionManager.DoAction(Abilities.Map[ClassJobType.Fisher][ability], ExProfileBehavior.Me);
		}

		#endregion Ability Checks and Actions

		#region Methods

		protected virtual bool ConditionCheck()
		{
			if (conditionFunc == null)
			{
				conditionFunc = ScriptManager.GetCondition(Condition);
			}

			return conditionFunc();
		}

		protected virtual bool MoochConditionCheck()
		{
			if (moochConditionFunc == null)
			{
				moochConditionFunc = ScriptManager.GetCondition(MoochCondition);
			}

			return moochConditionFunc();
		}

		protected virtual void Cast()
		{
			isFishIdentified = false;
			checkRelease = true;
			checkIdenticalCast = true;
			FishingManager.Cast();
			ResetMooch();
		}

		protected virtual void FaceFishSpot()
		{
			var i = MathEx.Random(0, 25);
			i = i / 100;

			var i2 = MathEx.Random(0, 100);

			if (i2 > 50)
			{
				ExProfileBehavior.Me.SetFacing(FishSpots.Current.Heading - (float)i);
			}
			else
			{
				ExProfileBehavior.Me.SetFacing(FishSpots.Current.Heading + (float)i);
			}
		}

		protected virtual void ChangeFishSpot()
		{
			FishSpots.Next();
			Logger.Info(Localization.Localization.ExFish_ChangeSpots);
			fishcount = 0;
			Logger.Info(Localization.Localization.ExFish_ResetCount);
			fishlimit = GetFishLimit();
			sitRoll = SitRng.NextDouble();
			spotinit = false;
			isFishing = false;
			isSitting = false;
		}

		protected virtual int GetFishLimit()
		{
			return Convert.ToInt32(MathEx.Random(MinimumFishPerSpot, MaximumFishPerSpot));
		}

		protected void ShuffleFishSpots()
		{
			if (Shuffle && FishSpots.Index == 0)
			{
				FishSpots.Shuffle();
				Logger.Info(Localization.Localization.ExFish_SuffleSpots);
			}
		}

		protected void ResetMooch()
		{
			if (mooch != 0)
			{
				mooch = 0;
				Logger.Info(Localization.Localization.ExFish_ResetMooch);
			}
		}

		protected void SetFishResult(string message)
		{
			var fishResult = new FishResult();
#if RB_CN
            var sizematch = FishSizeRegex.Match(message);
            var match = FishRegex.Matches(message);

            if (sizematch.Success)
            {
                fishResult.Name = match[1].ToString();
                float.TryParse(sizematch.Groups[1].Value, out float size);
                if (match[2].ToString() == "\uE03C")
                    fishResult.IsHighQuality = true;
#else
			var match = FishRegex.Match(message);

			if (match.Success)
			{
				fishResult.Name = match.Groups[1].Value;
				float.TryParse(match.Groups[2].Value, NumberStyles.Number, CultureInfo.InvariantCulture.NumberFormat, out var size);
				if (fishResult.Name[fishResult.Name.Length - 2] == ' ')
					fishResult.IsHighQuality = true;
#endif
				fishResult.Size = size;
			}
			FishResult = fishResult;
			isFishIdentified = true;
		}

		protected void ReceiveMessage(object sender, ChatEventArgs e)
		{
#if RB_CN
            if (e.ChatLogEntry.MessageType == (MessageType)2115 && e.ChatLogEntry.Contents.Contains("成功钓上了"))
#else
			if (e.ChatLogEntry.MessageType == (MessageType)2115 && e.ChatLogEntry.Contents.StartsWith("You land") && !e.ChatLogEntry.Contents.StartsWith("You land a fish usable with Mooch II"))
#endif
			{
				SetFishResult(e.ChatLogEntry.Contents);
			}

			if (e.ChatLogEntry.MessageType == (MessageType)2115
				&& e.ChatLogEntry.Contents.Equals(Localization.Localization.ExFish_NoSenceOfFish, StringComparison.InvariantCultureIgnoreCase))
			{
				Logger.Info(Localization.Localization.ExFish_NoSenceOfFish2);

				if (CanDoAbility(Ability.Quit))
				{
					DoAbility(Ability.Quit);
				}

				ChangeFishSpot();
			}

			if (e.ChatLogEntry.MessageType == (MessageType)2115
				&& e.ChatLogEntry.Contents == Localization.Localization.ExFish_AmissFish2)
			{
				Logger.Info(Localization.Localization.ExFish_AmissFish3);
				amissfish++;

				if (CanDoAbility(Ability.Quit))
				{
					DoAbility(Ability.Quit);
				}

				ChangeFishSpot();
			}
		}

		#endregion Methods
	}
}
