using p3ppc.visibleRankupReady.Configuration;
using p3ppc.visibleRankupReady.Template;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Memory;
using Reloaded.Memory.Interfaces;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace p3ppc.visibleRankupReady
{
    /// <summary>
    /// Your mod logic goes here.
    /// </summary>
    public unsafe class Mod : ModBase // <= Do not Remove.
    {
        /// <summary>
        /// Provides access to the mod loader API.
        /// </summary>
        private readonly IModLoader _modLoader;

        /// <summary>
        /// Provides access to the Reloaded.Hooks API.
        /// </summary>
        /// <remarks>This is null if you remove dependency on Reloaded.SharedLib.Hooks in your mod.</remarks>
        private readonly IReloadedHooks? _hooks;

        /// <summary>
        /// Provides access to the Reloaded logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Entry point into the mod, instance that created this class.
        /// </summary>
        private readonly IMod _owner;

        /// <summary>
        /// Provides access to this mod's configuration.
        /// </summary>
        private Config _configuration;

        /// <summary>
        /// The configuration of the currently executing mod.
        /// </summary>
        private readonly IModConfig _modConfig;

        private CheckSLinkLevelUpDelegate CheckSLinkLevelUp;
        private GetSpriteFromSprDelegate GetSpriteFromSpr;
        private RenderSpriteDelegate RenderSprite;
        private IReverseWrapper<SocialLinkSetupDelegate> _socialLinkSetupReverseWrapper;
        private IAsmHook _socialLinkSetupHook;
        private IReverseWrapper<SocialLinkSpriteSetupDelegate> _socialLinkSpriteSetupReverseWrapper;
        private IAsmHook _socialLinkSpriteSetupHook;
        private IHook<RenderSocialLinkInfoDelegate> _renderSocialLinkInfoHook;
        private IHook<RenderSocialLinkDetailsPageDelegate> _renderSocialLinkDetailsPageHook;
        private IAsmHook _detailsRankOffsetHook;
        private IAsmHook _detailsRankNumOffsetHook;
        private IAsmHook _detailsMaxOffsetHook;
        private IAsmHook _maxArcanaHook;
        private bool* _isFemc;
        private float* _detailsXOffset;
        private float* _maxXOffset;

        private nuint _rankupSprite;

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

            Utils.Initialise(_logger, _configuration);

            var memory = Memory.Instance;
            _detailsXOffset = (float*)memory.Allocate(4).Address;
            *_detailsXOffset = -8;
            Utils.LogDebug($"Wrote details x offset to 0x{(nuint)_detailsXOffset:X}");

            _maxXOffset = (float*)memory.Allocate(4).Address;
            *_maxXOffset = 6.5f;
            Utils.LogDebug($"Wrote max x offset to 0x{(nuint)_detailsXOffset:X}");

            var startupScannerController = _modLoader.GetController<IStartupScanner>();
            if (startupScannerController == null || !startupScannerController.TryGetTarget(out var startupScanner))
            {
                Utils.LogError($"Unable to get controller for Reloaded SigScan Library, stuff won't work :(");
                return;
            }

            startupScanner.AddMainModuleScan("48 89 6C 24 ?? 56 48 83 EC 20 0F B7 F1", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find CheckSLinkLevelUp, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found CheckSLinkLevelUp at 0x{result.Offset + Utils.BaseAddress:X}");

                CheckSLinkLevelUp = _hooks.CreateWrapper<CheckSLinkLevelUpDelegate>(Utils.BaseAddress + result.Offset, out _);
            });

            startupScanner.AddMainModuleScan("48 89 5C 24 ?? 57 48 83 EC 20 48 8B D9 8B FA 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 15 ?? ?? ?? ??", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find GetSpriteFromSpr, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found GetSpriteFromSpr at 0x{result.Offset + Utils.BaseAddress:X}");

                GetSpriteFromSpr = _hooks.CreateWrapper<GetSpriteFromSprDelegate>(Utils.BaseAddress + result.Offset, out _);
            });

            startupScanner.AddMainModuleScan("48 89 5C 24 ?? 57 48 83 EC 40 0F 29 74 24 ?? 48 8B D9 0F 29 7C 24 ?? 48 8D 0D ?? ?? ?? ?? 0F 28 F9 41 0F B6 F9", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find RenderSprite, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found RenderSprite at 0x{result.Offset + Utils.BaseAddress:X}");

                RenderSprite = _hooks.CreateWrapper<RenderSpriteDelegate>(Utils.BaseAddress + result.Offset, out _);
            });

            startupScanner.AddMainModuleScan("48 8B C4 55 53 56 57 41 54 41 55 41 56 41 57 48 8D 68 ?? 48 81 EC 38 01 00 00", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find RenderSocialLinkInfo, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found RenderSocialLinkInfo at 0x{result.Offset + Utils.BaseAddress:X}");

                _renderSocialLinkInfoHook = _hooks.CreateHook<RenderSocialLinkInfoDelegate>(RenderSocialLinkInfo, Utils.BaseAddress + result.Offset).Activate();
            });

            startupScanner.AddMainModuleScan("4C 8B DC 55 56 41 55", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find RenderSocialLinkDetailsPage, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found RenderSocialLinkDetailsPage at 0x{result.Offset + Utils.BaseAddress:X}");

                _renderSocialLinkDetailsPageHook = _hooks.CreateHook<RenderSocialLinkDetailsPageDelegate>(RenderSocialLinkDetailsPage, Utils.BaseAddress + result.Offset).Activate();
            });


            startupScanner.AddMainModuleScan("66 89 44 ?? ?? 66 83 F8 0A 75 ??", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find SetupSLinkMenu, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found SetupSLinkMenu at 0x{result.Offset + Utils.BaseAddress:X}");

                string[] function =
                {
                    "use64",
                    "push rcx\npush rax\npush r8\npush r9\npush r10\npush r11",
                    "lea rcx, [rdi+rcx*4+0x44]", // Put social link info into rcx
                    "sub rsp, 32",
                    $"{_hooks.Utilities.GetAbsoluteCallMnemonics(SocialLinkSetup, out _socialLinkSetupReverseWrapper)}",
                    "add rsp, 32",
                    "pop r11\npop r10\npop r9\npop r8\npop rax\npop rcx",
                };
                _socialLinkSetupHook = _hooks.CreateAsmHook(function, result.Offset + Utils.BaseAddress, AsmHookBehaviour.ExecuteFirst).Activate();
            });

            startupScanner.AddMainModuleScan("48 89 87 ?? ?? ?? ?? 4C 8B F0 48 85 C0", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find SetupSLinkMenuSprite, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found SetupSLinkMenuSprite at 0x{result.Offset + Utils.BaseAddress:X}");

                string[] function =
                {
                    "use64",
                    "push rcx\npush rax\npush r8\npush r9\npush r10\npush r11",
                    "mov rcx, rax",
                    "sub rsp, 32",
                    $"{_hooks.Utilities.GetAbsoluteCallMnemonics(SocialLinkSpriteSetup, out _socialLinkSpriteSetupReverseWrapper)}",
                    "add rsp, 32",
                    "pop r11\npop r10\npop r9\npop r8\npop rax\npop rcx",
                };
                _socialLinkSetupHook = _hooks.CreateAsmHook(function, result.Offset + Utils.BaseAddress, AsmHookBehaviour.ExecuteFirst).Activate();
            });

            startupScanner.AddMainModuleScan("E8 ?? ?? ?? ?? 48 63 05 ?? ?? ?? ?? 45 33 C0 F3 44 0F 59 25 ?? ?? ?? ??", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find SLDetailsRankOffset, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found SLDetailsRankOffset at 0x{result.Offset + Utils.BaseAddress:X}");

                string[] function =
                {
                    "use64",
                    "push rax",
                    "push rbx",
                    // Get sl index
                    "movsx eax, word [rsi+0x2E]",
                    "movsx ebx, word [rsi+0x2C]",
                    "add eax, ebx",
                    "lea rax,[rax+rax*2]",
                    // Get is rankup ready
                    "movzx eax, word [rsi + rax*4 + 0x4A]",
                    // Check if rankup reaedy
                    "cmp eax, 0",
                    "je noRankup",
                    // Offset xPos
                    $"addss xmm1, [qword {(nuint)_detailsXOffset}]",
                    "label noRankup",
                    "pop rbx",
                    "pop rax"
                };
                _detailsRankOffsetHook = _hooks.CreateAsmHook(function, result.Offset + Utils.BaseAddress, AsmHookBehaviour.ExecuteFirst).Activate();
            });

            startupScanner.AddMainModuleScan("E8 ?? ?? ?? ?? 4C 8D 2D ?? ?? ?? ?? 0F B6 9E ?? ?? ?? ??", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find SLDetailsRankNumOffset, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found SLDetailsRankNumOffset at 0x{result.Offset + Utils.BaseAddress:X}");

                string[] function =
                {
                    "use64",
                    "push rax",
                    "push rbx",
                    // Get sl index
                    "movsx eax, word [rsi+0x2E]",
                    "movsx ebx, word [rsi+0x2C]",
                    "add eax, ebx",
                    "lea rax,[rax+rax*2]",
                    // Get is rankup ready
                    "movzx eax, word [rsi + rax*4 + 0x4A]",
                    // Check if rankup reaedy
                    "cmp eax, 0",
                    "je noRankup",
                    // Offset xPos
                    $"addss xmm1, [qword {(nuint)_detailsXOffset}]",
                    "label noRankup",
                    "pop rbx",
                    "pop rax"
                };
                _detailsRankNumOffsetHook = _hooks.CreateAsmHook(function, result.Offset + Utils.BaseAddress, AsmHookBehaviour.ExecuteFirst).Activate();
            });

            // Fixes the positioning of the Max text in the details menu (it's wrong in vanilla)
            startupScanner.AddMainModuleScan("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? F3 0F 10 3D ?? ?? ?? ?? 48 8D 05 ?? ?? ?? ??", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find SLDetailsMaxOffset, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found SLDetailsMaxOffset at 0x{result.Offset + Utils.BaseAddress:X}");

                string[] function =
                {
                    "use64",
                    $"addss xmm1, [qword {(nuint)_maxXOffset}]",
                };
                _detailsMaxOffsetHook = _hooks.CreateAsmHook(function, result.Offset + Utils.BaseAddress, AsmHookBehaviour.ExecuteFirst).Activate();
            });

            // Fixes the Arcana name of Maxed links being the wrong colour (broken in vanilla)
            startupScanner.AddMainModuleScan("42 8B 8C ?? ?? ?? ?? ?? 89 4C 24 ?? 89 4D ?? 0F B6 4C 24 ??", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find MaxArcanaNameColour, stuff won't work :(");
                    return;
                }
                Utils.LogDebug($"Found MaxArcanaNameColour at 0x{result.Offset + Utils.BaseAddress:X}");

                memory.Read((nuint)(Utils.BaseAddress + result.Offset + 4), out int colourAddress);

                // Change the colour when it's selected
                memory.SafeWrite((nuint)(result.Offset + Utils.BaseAddress - 36), BitConverter.GetBytes(colourAddress));

                // Change the colour when it isn't selected
                string[] function =
                {
                    "use64",
                    "push rcx",
                    $"mov ecx,[rcx+r8+{colourAddress-4}]",
                    "mov [rsp+0x84], ecx",
                    "pop rcx"
                };
                _maxArcanaHook = _hooks.CreateAsmHook(function, result.Offset + Utils.BaseAddress, AsmHookBehaviour.ExecuteFirst).Activate();
            });


            startupScanner.AddMainModuleScan("48 8D 35 ?? ?? ?? ?? 0F 28 05 ?? ?? ?? ??", result =>
            {
                if (!result.Found)
                {
                    Utils.LogError($"Unable to find IsFemc, stuff won't be coloured right :(");
                    return;
                }

                _isFemc = (bool*)Utils.GetGlobalAddress(Utils.BaseAddress + result.Offset - 4);
                Utils.LogDebug($"Found IsFemc at 0x{(nuint)_isFemc:X}");
            });

        }

        private void SocialLinkSetup(SocialLinkInfo* info)
        {
            var sl = info->SocialLink;
            if (sl == SocialLink.SEES || sl == SocialLink.Pharos)
                info->RankUpReady = false; // Auto S.Links
            else
                info->RankUpReady = CheckSLinkLevelUp(info->SocialLink);
        }

        private void SocialLinkSpriteSetup(nuint spr)
        {
            _rankupSprite = GetSpriteFromSpr(spr, 101);
        }

        // An individual item in the S.Link menu (the list of them)
        private void RenderSocialLinkInfo(int index, SocialLinkMenuInfo* info)
        {
            _renderSocialLinkInfoHook.OriginalFunction(index, info);

            int slIndex = index + info->ListOffset;
            if ((&info->SocialLinks)[slIndex].RankUpReady)
            {
                var alpha = (&info->AlphaThings)[index].Alpha * (info->BaseAlpha / 255.0f); // This is how the game calculates it, I don't really understand
                var colour = info->SelectedIndex == index ? SelectedColour : NormalColour[*_isFemc ? 1 : 0];
                RenderSprite(_rankupSprite, 97, 60 + index * 37, colour.R, colour.G, colour.B, (byte)alpha);
            }
        }

        // A specific S.Link that has been selected
        private void RenderSocialLinkDetailsPage(SocialLinkMenuInfo* info, nuint param_2, nuint param_3, nuint param_4)
        {
            _renderSocialLinkDetailsPageHook.OriginalFunction(info, param_2, param_3, param_4);

            int slIndex = info->SelectedIndex + info->ListOffset;
            if ((&info->SocialLinks)[slIndex].RankUpReady)
            {
                var alpha = info->DetailsAlpha * (info->BaseAlpha / 255.0f); // This is how the game calculates it, I don't really understand
                RenderSprite(_rankupSprite, 77, 9, 0xFF, 0xFF, 0xFF, (byte)alpha);
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        struct SocialLinkMenuInfo
        {
            [FieldOffset(0)]
            internal byte BaseAlpha;

            [FieldOffset(44)]
            internal short SelectedIndex;

            [FieldOffset(46)]
            internal short ListOffset;

            // Has 28 items
            [FieldOffset(68)]
            internal SocialLinkInfo SocialLinks;

            [FieldOffset(506)]
            internal SocialLinkMenuInfoThing AlphaThings;

            [FieldOffset(2186)]
            internal byte DetailsAlpha;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct SocialLinkInfo
        {
            [FieldOffset(0)]
            internal short Arcana;

            [FieldOffset(2)]
            internal SocialLink SocialLink;

            [FieldOffset(4)]
            internal short Rank;

            // An unused field we're using for the mod
            [FieldOffset(6)]
            internal bool RankUpReady;

            [FieldOffset(8)]
            internal int Status;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct SocialLinkMenuInfoThing
        {
            [FieldOffset(0)]
            internal byte Alpha;

            [FieldOffset(47)]
            byte dummy; // Make the struct 48 long
        }

        struct Colour
        {
            internal byte R;
            internal byte G;
            internal byte B;
            internal byte A;
        }

        private readonly Colour SelectedColour = new Colour { R = 0xFF, G = 0xFF, B = 0xFF, A = 0xFF }; 

        private readonly Colour[] NormalColour = new Colour[]
        {
            new Colour { R = 0x00, G = 0x3C, B = 0x5F, A = 0xFF }, // Male
            new Colour { R = 0x6B, G = 0x01, B = 0x0A, A = 0xFF } // Female
        };

        enum SocialLink
        {
            None = 0,
            SEES = 1,
            Kenji = 2,
            Hidetoshi = 3,
            OldCouple = 4,
            Yukari = 5,
            Fuuka = 6,
            Mitsuru = 7,
            Aigis = 8,
            Yuko = 9,
            Chihiro = 10,
            Kazushi_Track = 11,
            Kazushi_Keendo = 12,
            Kazushi_Swim = 13,
            Maya = 14,
            Keisuke_Photography = 15,
            Keisuke_Art = 16,
            Keeisuke_Music = 17,
            Maiko = 18,
            Pharos = 19,
            Bebe = 20,
            Tanaka = 21,
            Mutatsu = 22,
            Mamoru = 23,
            Akinari = 24,
            Judgement = 25,
        }

        [Function(CallingConventions.Microsoft)]
        private delegate bool CheckSLinkLevelUpDelegate(SocialLink id);

        [Function(CallingConventions.Microsoft)]
        private delegate nuint GetSpriteFromSprDelegate(nuint spr, int spriteId);

        [Function(CallingConventions.Microsoft)]
        private delegate nuint RenderSpriteDelegate(nuint spr, float xPos, float yPos, byte R, byte G, byte B, byte A);

        [Function(CallingConventions.Microsoft)]
        private delegate void RenderSocialLinkInfoDelegate(int index, SocialLinkMenuInfo* info);

        [Function(CallingConventions.Microsoft)]
        private delegate void RenderSocialLinkDetailsPageDelegate(SocialLinkMenuInfo* info, nuint param_2, nuint param_3, nuint param_4);

        [Function(CallingConventions.Microsoft)]
        private delegate void SocialLinkSetupDelegate(SocialLinkInfo* info);

        [Function(CallingConventions.Microsoft)]
        private delegate void SocialLinkSpriteSetupDelegate(nuint spr);

        #region Standard Overrides
        public override void ConfigurationUpdated(Config configuration)
        {
            // Apply settings from configuration.
            // ... your code here.
            _configuration = configuration;
            _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}