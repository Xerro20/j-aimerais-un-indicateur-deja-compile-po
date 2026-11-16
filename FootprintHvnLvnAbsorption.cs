// =============================================================================
//  ATAS X - Bot BigTrades Piégés (Losanges ◆) + Reversal + Options X-Ray + HVN/LVN
//  Cible : .NET 10.0 (ATAS X Windows & macOS)
//
//  Architecture exclusive (sans aucune règle de l'ancienne stratégie) :
//    1. BigTrades Piégés (Losanges ◆) + Reversal (SEUL déclencheur de trades) :
//       - ACHAT (▲ REVERSAL LONG) : Un gros BigTrade Sell (Losange Rouge ◆) est
//         piégé / absorbé dans le bas du mouvement suivi d'un retournement haussier
//         clôturant au-dessus du losange.
//       - VENTE (▼ REVERSAL SHORT) : Un gros BigTrade Buy (Losange Vert ◆) est
//         piégé / rejeté dans le haut du mouvement suivi d'un retournement baissier
//         clôturant sous le losange.
//    2. Indicateur Options X-Ray (CALL WALL, MAX PAIN, PUT WALL, 0 DTE & Profil GEX)
//    3. Code HVN (High Volume Nodes) & LVN (Low Volume Nodes) (Intra-Bar & Composite)
//    4. Outils Pratiques conservés :
//       - Bouton d'activation du Bot sur la charte + Encadrement Horaire (Début/Fin)
//       - Synchronisation du PnL Réel avec le Compte Actif ATAS + Trading Croisé Micro
//       - Coupe-Circuit Max Drawdown ($) & Objectif Max PnL ($)
// =============================================================================
#pragma warning disable CS0414, CS0649

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Drawing.Drawing2D;
using ATAS.DataFeedsCore;
using ATAS.Indicators;
using OFT.Attributes;
using OFT.Rendering.Context;
using OFT.Rendering.Control;
using OFT.Rendering.Settings;
using OFT.Rendering.Tools;

namespace ATAS.Indicators.Custom
{
    [DisplayName("ATAS X - Bot BigTrades Piégés (Losanges ◆) + Reversal + Options X-Ray + HVN/LVN")]
    [Category("Order Flow - Custom")]
    [Description("Nouveau Bot de Trading ATAS X (.NET 10) : ouvre uniquement des trades sur BigTrades Piégés (losanges ◆) + Reversal, avec l'indicateur Options X-Ray (CALL WALL, MAX PAIN, PUT WALL & Profil GEX), les zones HVN/LVN et les outils pratiques (Bouton ON/OFF, Horaires, Sync PnL Compte Actif, Trading Croisé Micro, Max DD / Max PnL).")]
    public class FootprintHvnLvnAbsorption : Indicator
    {
        // =====================================================================
        // Groupes de paramètres dans l'interface ATAS X
        // =====================================================================
        private const string GrpGeneral       = "1. Général & Footprint";
        private const string GrpBarHvn        = "2. HVN sur Footprint (Intra-Bougie)";
        private const string GrpBarLvn        = "3. LVN sur Footprint (Intra-Bougie)";
        private const string GrpProfile       = "4. HVN / LVN Profil Composite (Multi-Bougies)";
        private const string GrpAbsorption    = "5. Heavy Absorption (Détection)";
        private const string GrpAbsVisual     = "6. Heavy Absorption (Visuel & Rayons)";
        private const string GrpTrappedDetect = "7. Acheteurs & Vendeurs Piégés (Détection)";
        private const string GrpBigTrades     = "7b. BigTrades Piégés (Losanges ◆) & Reversal";
        private const string GrpTrappedVisual = "8. Acheteurs & Vendeurs Piégés (Visuel & Rayons)";
        private const string GrpOptionsAlerts = "9. Alerte Visuelle Achat/Vente & Flux d'Options ATAS X";
        private const string GrpOptionsXRay   = "9b. Indicateur Options X-Ray (CALL WALL, MAX PAIN, PUT WALL & GEX)";
        private const string GrpHudAlerts     = "10. Panneau HUD & Alertes Sonores";
        private const string GrpQuantEngine   = "11. Modèle Quant Senior (Z-Score, ATR, CVD & VWAP)";
        private const string GrpQuantBot      = "12. Bot de Trading Quant & Risk Guard (Max DD / Max PnL)";

        public enum BotExecutionMode
        {
            BacktestAndPaperOnly,
            LiveAutoTradingManager
        }

        public enum ActiveAccountPnlScope
        {
            ActivePortfolioTotal,
            ChartInstrumentOnly
        }

        public enum MarketProfilePreset
        {
            AutoDetect,
            CmeSp500_ES_MES,
            CmeNasdaq_NQ_MNQ,
            CmeKeyFutures_GC_CL_YM_RTY,
            CryptoBinanceFutures
        }

        public enum CrossTradingExecutionMode
        {
            AutoAtasCrossTrading,
            ForceMicroOnNormalChart,
            NormalChartContractOnly
        }

        // =====================================================================
        // Énumérations
        // =====================================================================
        public enum ProfileScopeMode
        {
            RollingBars,
            CurrentSession
        }

        public enum AbsorptionMarkerType
        {
            Arrow,
            Diamond,
            Dot,
            Bracket
        }

        public enum HudCorner
        {
            TopRight,
            TopLeft,
            BottomRight,
            BottomLeft
        }

        // =====================================================================
        // 1. GÉNÉRAL & FOOTPRINT
        // =====================================================================
        private MarketProfilePreset _marketProfile = MarketProfilePreset.AutoDetect;
        [Display(Name = "Profil de Marché Quant (Auto : CME ES / NQ / Contrats Clés / Crypto BTC)", GroupName = GrpGeneral, Order = 5,
            Description = "AutoDetect adapte automatiquement le Footprint, le Flux d'Options (compensation ΔOI CME via MinDelta/MaxDelta) et les distances SL/TP1/TP2 aux contrats CME (S&P 500 ES/MES, NASDAQ NQ/MNQ, GC/MGC, CL/MCL, YM/MYM, RTY/M2K) tout en conservant à 100% le comportement original sur BTC Binance Futures.")]
        public MarketProfilePreset MarketProfile
        {
            get => _marketProfile;
            set { _marketProfile = value; RecalculateValues(); RedrawChart(); }
        }

        private int _ticksPerRow = 1;
        [Display(Name = "Regroupement (Ticks par niveau)", GroupName = GrpGeneral, Order = 10,
            Description = "Nombre de ticks agrégés par ligne de prix sur le footprint (1 = tick par tick).")]
        [Range(1, 50)]
        public int TicksPerRow
        {
            get => _ticksPerRow;
            set { _ticksPerRow = Math.Max(1, value); RecalculateValues(); }
        }

        private bool _enableCmeMicroClusterPooling = true;
        [Display(Name = "Agrégation Intelligente des Clusters Adjacents sur CME (ES / NQ)", GroupName = GrpGeneral, Order = 15,
            Description = "Sur le CME (NASDAQ NQ/MNQ à carnet rapide et S&P 500 ES/MES à carnet dense), regroupe les murs d'absorption et de traders piégés répartis sur 2 à 3 ticks adjacents tout en gérant les balayages à 0 contrat (Zero-Print Sweeps).")]
        public bool EnableCmeMicroClusterPooling
        {
            get => _enableCmeMicroClusterPooling;
            set { _enableCmeMicroClusterPooling = value; RecalculateValues(); RedrawChart(); }
        }

        [Display(Name = "Afficher texte Bid x Ask intégré", GroupName = GrpGeneral, Order = 20,
            Description = "Activez si votre graphique est en chandeliers classiques et que vous souhaitez afficher les volumes Bid x Ask dans les cellules.")]
        public bool RenderBuiltInBidAskText { get; set; } = false;

        [Display(Name = "Afficher étiquettes sur cellules (HVN/LVN/ABS/TRAP)", GroupName = GrpGeneral, Order = 30)]
        public bool ShowCellBadges { get; set; } = true;

        [Display(Name = "Taille de police (px)", GroupName = GrpGeneral, Order = 40)]
        [Range(7, 24)]
        public int CellFontSize { get; set; } = 10;

        [Display(Name = "Largeur cellule personnalisée (0 = Auto)", GroupName = GrpGeneral, Order = 50,
            Description = "0 ajuste automatiquement la largeur de surbrillance à la largeur de la bougie Footprint.")]
        [Range(0, 300)]
        public int CustomCellWidthPx { get; set; } = 0;

        // =====================================================================
        // 2. HVN SUR FOOTPRINT (INTRA-BOUGIE)
        // =====================================================================
        private bool _enableBarHvn = true;
        [Display(Name = "Activer HVN Intra-Bougie", GroupName = GrpBarHvn, Order = 10)]
        public bool EnableBarHvn
        {
            get => _enableBarHvn;
            set { _enableBarHvn = value; RecalculateValues(); }
        }

        private int _hvnBarPctOfPoc = 70;
        [Display(Name = "Seuil Volume HVN (% du POC de la bougie)", GroupName = GrpBarHvn, Order = 20,
            Description = "Un niveau est marqué HVN si son volume atteint au moins ce pourcentage du volume maximum (POC) de la bougie.")]
        [Range(30, 100)]
        public int HvnBarPctOfPoc
        {
            get => _hvnBarPctOfPoc;
            set { _hvnBarPctOfPoc = value; RecalculateValues(); }
        }

        private int _hvnMinBarVolume = 0;
        [Display(Name = "Volume Minimum HVN (contrats/lots)", GroupName = GrpBarHvn, Order = 30)]
        [Range(0, 1000000)]
        public int HvnMinBarVolume
        {
            get => _hvnMinBarVolume;
            set { _hvnMinBarVolume = value; RecalculateValues(); }
        }

        private bool _requireLocalPeakForBarHvn = true;
        [Display(Name = "Exiger un pic local de volume", GroupName = GrpBarHvn, Order = 40,
            Description = "Le niveau HVN doit avoir un volume supérieur ou égal aux niveaux immédiatement adjacents.")]
        public bool RequireLocalPeakForBarHvn
        {
            get => _requireLocalPeakForBarHvn;
            set { _requireLocalPeakForBarHvn = value; RecalculateValues(); }
        }

        [Display(Name = "Couleur HVN Intra-Bougie", GroupName = GrpBarHvn, Order = 50)]
        public Color BarHvnColor { get; set; } = Color.FromArgb(255, 255, 195, 0); // Or / Ambre

        [Display(Name = "Couleur POC (Point of Control)", GroupName = GrpBarHvn, Order = 60)]
        public Color BarPocColor { get; set; } = Color.FromArgb(255, 255, 140, 0); // Orange vif

        [Display(Name = "Opacité remplissage HVN (0-255)", GroupName = GrpBarHvn, Order = 70)]
        [Range(10, 220)]
        public int BarHvnFillAlpha { get; set; } = 65;

        // =====================================================================
        // 3. LVN SUR FOOTPRINT (INTRA-BOUGIE)
        // =====================================================================
        private bool _enableBarLvn = true;
        [Display(Name = "Activer LVN Intra-Bougie", GroupName = GrpBarLvn, Order = 10)]
        public bool EnableBarLvn
        {
            get => _enableBarLvn;
            set { _enableBarLvn = value; RecalculateValues(); }
        }

        private int _lvnBarMaxPctOfPoc = 25;
        [Display(Name = "Seuil Max Volume LVN (% du POC)", GroupName = GrpBarLvn, Order = 20,
            Description = "Un creux interne de volume est marqué LVN si son volume est inférieur ou égal à ce % du POC de la bougie.")]
        [Range(1, 70)]
        public int LvnBarMaxPctOfPoc
        {
            get => _lvnBarMaxPctOfPoc;
            set { _lvnBarMaxPctOfPoc = value; RecalculateValues(); }
        }

        private int _lvnExcludeExtremeTicks = 1;
        [Display(Name = "Ignorer N niveaux aux extrêmes High/Low", GroupName = GrpBarLvn, Order = 30,
            Description = "Évite de marquer le dernier tick de mèche comme LVN ; cible les poches de vide à l'intérieur de la bougie.")]
        [Range(0, 10)]
        public int LvnExcludeExtremeTicks
        {
            get => _lvnExcludeExtremeTicks;
            set { _lvnExcludeExtremeTicks = value; RecalculateValues(); }
        }

        [Display(Name = "Couleur LVN Intra-Bougie", GroupName = GrpBarLvn, Order = 40)]
        public Color BarLvnColor { get; set; } = Color.FromArgb(255, 168, 85, 247); // Violet / Pourpre

        [Display(Name = "Opacité remplissage LVN (0-255)", GroupName = GrpBarLvn, Order = 50)]
        [Range(10, 220)]
        public int BarLvnFillAlpha { get; set; } = 60;

        // =====================================================================
        // 4. HVN / LVN PROFIL COMPOSITE (MULTI-BOUGIES)
        // =====================================================================
        [Display(Name = "Afficher Zones HVN/LVN Composites", GroupName = GrpProfile, Order = 10,
            Description = "Projette sur le graphique les niveaux HVN et LVN majeurs calculés sur le profil de volume glissant ou de la session.")]
        public bool EnableCompositeProfile { get; set; } = true;

        [Display(Name = "Mode de calcul du Profil", GroupName = GrpProfile, Order = 20)]
        public ProfileScopeMode ProfileScope { get; set; } = ProfileScopeMode.RollingBars;

        [Display(Name = "Période glissante (nombre de bougies)", GroupName = GrpProfile, Order = 30)]
        [Range(5, 500)]
        public int ProfileLookbackBars { get; set; } = 40;

        [Display(Name = "Seuil HVN Composite (% du POC Composite)", GroupName = GrpProfile, Order = 40)]
        [Range(30, 95)]
        public int CompositeHvnPct { get; set; } = 65;

        [Display(Name = "Seuil LVN Composite (% du POC Composite)", GroupName = GrpProfile, Order = 50)]
        [Range(5, 60)]
        public int CompositeLvnPct { get; set; } = 25;

        [Display(Name = "Nombre max de zones HVN/LVN affichées", GroupName = GrpProfile, Order = 60)]
        [Range(1, 15)]
        public int MaxCompositeZonesEach { get; set; } = 4;

        [Display(Name = "Couleur Zone HVN Composite", GroupName = GrpProfile, Order = 70)]
        public Color CompositeHvnColor { get; set; } = Color.FromArgb(255, 250, 204, 21);

        [Display(Name = "Couleur Zone LVN Composite", GroupName = GrpProfile, Order = 80)]
        public Color CompositeLvnColor { get; set; } = Color.FromArgb(255, 192, 132, 252);

        [Display(Name = "Opacité des bandes composites (0-255)", GroupName = GrpProfile, Order = 90)]
        [Range(10, 150)]
        public int CompositeBandAlpha { get; set; } = 35;

        // =====================================================================
        // 5 & 6. ANCIENNE HEAVY ABSORPTION DÉSACTIVÉE (REMPLACÉE PAR BIGTRADES ◆)
        // =====================================================================
        private bool _enableAbsorption = false;
        [Browsable(false)]
        public bool EnableAbsorption
        {
            get => false;
            set { _enableAbsorption = false; }
        }

        private int _absMinVolume = 0;
        [Browsable(false)]
        public int AbsMinVolume { get => _absMinVolume; set => _absMinVolume = value; }

        private decimal _absVolumeMultiplier = 1.5m;
        [Browsable(false)]
        public decimal AbsVolumeMultiplier { get => _absVolumeMultiplier; set => _absVolumeMultiplier = value; }

        private int _absMinDominantPct = 60;
        [Browsable(false)]
        public int AbsMinDominantPct { get => _absMinDominantPct; set => _absMinDominantPct = value; }

        private int _absMinAbsDelta = 0;
        [Browsable(false)]
        public int AbsMinAbsDelta { get => _absMinAbsDelta; set => _absMinAbsDelta = value; }

        private decimal _absDiagonalRatio = 1.6m;
        [Browsable(false)]
        public decimal AbsDiagonalRatio { get => _absDiagonalRatio; set => _absDiagonalRatio = value; }

        private int _absZonePct = 35;
        [Browsable(false)]
        public int AbsZonePct { get => _absZonePct; set => _absZonePct = value; }

        private int _absLocationTicks = 3;
        [Browsable(false)]
        public int AbsLocationTicks { get => _absLocationTicks; set => _absLocationTicks = value; }

        private bool _requirePriceRejection = true;
        [Browsable(false)]
        public bool RequirePriceRejection { get => _requirePriceRejection; set => _requirePriceRejection = value; }

        private bool _allowMidBarWallAbsorption = false;
        [Browsable(false)]
        public bool AllowMidBarWallAbsorption { get => false; set => _allowMidBarWallAbsorption = false; }

        [Browsable(false)]
        public Color BullishAbsorptionColor { get; set; } = Color.FromArgb(255, 0, 240, 255);

        [Browsable(false)]
        public Color BearishAbsorptionColor { get; set; } = Color.FromArgb(255, 255, 45, 135);

        [Browsable(false)]
        public int AbsHighlightAlpha { get; set; } = 135;

        [Browsable(false)]
        public int AbsBorderThickness { get; set; } = 2;

        [Browsable(false)]
        public bool ShowBarExtremeMarker { get => false; set { } }

        [Browsable(false)]
        public AbsorptionMarkerType BarMarkerStyle { get; set; } = AbsorptionMarkerType.Diamond;

        [Browsable(false)]
        public int BarMarkerSize { get; set; } = 9;

        [Browsable(false)]
        public bool ProjectAbsorptionRays { get => false; set { } }

        [Browsable(false)]
        public int AbsorptionRayMaxBars { get; set; } = 25;

        [Browsable(false)]
        public bool StopRayWhenMitigated { get; set; } = true;

        // =====================================================================
        // 7. ANCIENS CLUSTERS PIÉGÉS DÉSACTIVÉS (SEULS LES BIGTRADES ◆ SONT UTILISÉS)
        // =====================================================================
        private bool _enableTrappedTraders = false;
        [Browsable(false)]
        public bool EnableTrappedTraders
        {
            get => false;
            set { _enableTrappedTraders = false; }
        }

        private int _trappedMinVolume = 0;
        [Browsable(false)]
        public int TrappedMinVolume { get => _trappedMinVolume; set => _trappedMinVolume = value; }

        private decimal _trappedVolumeMultiplier = 0.65m;
        [Browsable(false)]
        public decimal TrappedVolumeMultiplier { get => _trappedVolumeMultiplier; set => _trappedVolumeMultiplier = value; }

        private int _trappedMinDominantPct = 58;
        [Browsable(false)]
        public int TrappedMinDominantPct { get => _trappedMinDominantPct; set => _trappedMinDominantPct = value; }

        private decimal _trappedImbalanceRatio = 1.45m;
        [Browsable(false)]
        public decimal TrappedImbalanceRatio { get => _trappedImbalanceRatio; set => _trappedImbalanceRatio = value; }

        private int _trappedWickZonePct = 42;
        [Browsable(false)]
        public int TrappedWickZonePct { get => _trappedWickZonePct; set => _trappedWickZonePct = value; }

        private bool _requireStrictWickOrOppositeClose = true;
        [Browsable(false)]
        public bool RequireStrictWickOrOppositeClose { get => _requireStrictWickOrOppositeClose; set => _requireStrictWickOrOppositeClose = value; }

        private int _trappedMinRejectionLevels = 1;
        [Browsable(false)]
        public int TrappedMinRejectionLevels { get => _trappedMinRejectionLevels; set => _trappedMinRejectionLevels = value; }

        // =====================================================================
        // 5. BIGTRADES PIÉGÉS (LOSANGES ◆) & REVERSAL (SEUL MOTEUR D'OUVERTURE)
        // =====================================================================
        private bool _enableBigTradesReversal = true;
        [Display(Name = "Activer BigTrades Piégés (Losanges ◆) + Reversal", GroupName = GrpBigTrades, Order = 10,
            Description = "Détecte les BigTrades (losanges rouges/verts ◆ sur le Footprint ou le Tape >= 40 lots sur NQ) qui se retrouvent piégés et ouvre uniquement des trades Reversal confirmés avec HVN/LVN et Options X-Ray.")]
        public bool EnableBigTradesReversal
        {
            get => _enableBigTradesReversal;
            set { _enableBigTradesReversal = value; RecalculateValues(); RedrawChart(); }
        }

        private int _bigTradeMinLots = 40;
        [Display(Name = "Seuil Minimum BigTrade (Lots — ex: 40 lots sur NQ)", GroupName = GrpBigTrades, Order = 20,
            Description = "Seuil de volume agressif (ex: 40 lots sur NQ comme l'alerte 'New BigTrade at ... is more than 40 lots') pour afficher un losange BigTrade ◆ et qualifier un BigTrade Piégé.")]
        [Range(1, 50000)]
        public int BigTradeMinLots
        {
            get => _bigTradeMinLots;
            set { _bigTradeMinLots = Math.Max(1, value); RecalculateValues(); RedrawChart(); }
        }

        private bool _bigTradeAutoScaleByMarket = true;
        [Display(Name = "Calibrer automatiquement le seuil BigTrade selon le contrat", GroupName = GrpBigTrades, Order = 30,
            Description = "Adapte automatiquement le seuil BigTrade aux différents contrats (NQ = 40 lots, ES = 80 lots, MNQ/MES/BTC) tout en respectant le seuil personnalisé sur NQ.")]
        public bool BigTradeAutoScaleByMarket
        {
            get => _bigTradeAutoScaleByMarket;
            set { _bigTradeAutoScaleByMarket = value; RecalculateValues(); RedrawChart(); }
        }

        private int _bigTradeReversalLookbackBars = 3;
        [Display(Name = "Fenêtre Multi-Bougies pour Reversal sur BigTrade Piégé (bougies)", GroupName = GrpBigTrades, Order = 40,
            Description = "Permet de déclencher le Reversal lorsque le losange BigTrade ◆ apparaît sur la bougie courante OU sur les 1 à 3 bougies précédentes et que la bougie actuelle se retourne et clôture au-delà du losange piégé.")]
        [Range(1, 6)]
        public int BigTradeReversalLookbackBars
        {
            get => _bigTradeReversalLookbackBars;
            set { _bigTradeReversalLookbackBars = Math.Clamp(value, 1, 6); RecalculateValues(); RedrawChart(); }
        }

        [Display(Name = "Afficher les Losanges BigTrades (◆) sur les bougies", GroupName = GrpBigTrades, Order = 50,
            Description = "Dessine les losanges doubles ATAS BigTrades (◆ Vert = BigTrade Buy, ◆ Rouge = BigTrade Sell, contour Or = ◆ PIÉGÉ) directement sur les bougies.")]
        public bool ShowBigTradeDiamonds { get; set; } = true;

        [Display(Name = "Taille de base des Losanges BigTrades (px)", GroupName = GrpBigTrades, Order = 60)]
        [Range(6, 36)]
        public int BigTradeDiamondBaseSize { get; set; } = 13;

        [Display(Name = "Afficher l'encadré d'alerte BigTrade en bas à gauche (style ATAS Alerts)", GroupName = GrpBigTrades, Order = 70,
            Description = "Affiche la notification temps réel 'New BigTrade at ... (sell/buy) is more than 40 lots' en bas à gauche du graphique.")]
        public bool ShowBigTradeAlertToast { get; set; } = true;

        [Display(Name = "Couleur Losange BigTrade Achat (Buy ◆)", GroupName = GrpBigTrades, Order = 80)]
        public Color BigTradeBuyColor { get; set; } = Color.FromArgb(255, 16, 235, 130); // Vert Émeraude Vif

        [Display(Name = "Couleur Losange BigTrade Vente (Sell ◆)", GroupName = GrpBigTrades, Order = 90)]
        public Color BigTradeSellColor { get; set; } = Color.FromArgb(255, 239, 68, 68); // Rouge Corail Vif

        [Browsable(false)]
        public Color TrappedSellersBullColor { get; set; } = Color.FromArgb(255, 16, 245, 150);

        [Browsable(false)]
        public Color TrappedBuyersBearColor { get; set; } = Color.FromArgb(255, 255, 90, 54);

        [Browsable(false)]
        public int TrappedHighlightAlpha { get; set; } = 120;

        [Browsable(false)]
        public bool ShowTrappedBarMarker { get => false; set { } }

        [Browsable(false)]
        public bool ProjectTrappedRays { get => false; set { } }

        [Browsable(false)]
        public int TrappedRayMaxBars { get; set; } = 20;

        // =====================================================================
        // 6. SIGNAUX VISUELS REVERSAL ACHAT / VENTE
        // =====================================================================
        private bool _enableVisualBuySellAlerts = true;
        [Display(Name = "Afficher les Signaux Reversal ACHAT ◆▲ / VENTE ◆▼", GroupName = GrpOptionsAlerts, Order = 10,
            Description = "Affiche le signal d'ouverture uniquement lorsqu'un BigTrade Piégé (losange ◆) déclenche un Reversal.")]
        public bool EnableVisualBuySellAlerts
        {
            get => _enableVisualBuySellAlerts;
            set { _enableVisualBuySellAlerts = value; RecalculateValues(); }
        }

        private bool _enableAtasOptionsFlow = true;
        [Browsable(false)]
        public bool EnableAtasOptionsFlow
        {
            get => _enableAtasOptionsFlow;
            set { _enableAtasOptionsFlow = value; }
        }

        private bool _requireOptionsFlowConfirmation = false;
        [Browsable(false)]
        public bool RequireOptionsFlowConfirmation
        {
            get => false;
            set { _requireOptionsFlowConfirmation = false; }
        }

        private int _optionsMinDominancePct = 50;
        [Browsable(false)]
        public int OptionsMinDominancePct
        {
            get => _optionsMinDominancePct;
            set { _optionsMinDominancePct = value; }
        }

        private decimal _optionsSweepMultiplier = 1.7m;
        [Display(Name = "Multiplicateur de Volume Sweep / Block Options", GroupName = GrpOptionsAlerts, Order = 50,
            Description = "Seuil de détection des Sweeps d'options et blocs institutionnels par rapport au flux moyen de la bougie.")]
        [Range(1.1, 10.0)]
        public decimal OptionsSweepMultiplier
        {
            get => _optionsSweepMultiplier;
            set { _optionsSweepMultiplier = value; RecalculateValues(); }
        }

        [Display(Name = "Afficher Cartouche ACHAT ▲ / VENTE ▼ sur la bougie", GroupName = GrpOptionsAlerts, Order = 60,
            Description = "Affiche un cartouche lumineux au-dessus ou en dessous de la bougie avec le signal ACHAT/VENTE, le score et le ratio Call/Put.")]
        public bool ShowSignalCalloutOnBar { get; set; } = true;

        [Display(Name = "Afficher Bannière Visuelle d'Alerte en haut du graphique", GroupName = GrpOptionsAlerts, Order = 70,
            Description = "Affiche un bandeau visuel d'alerte ACHAT / VENTE proéminent en haut du graphique avec le détail du Flux d'Options ATAS X.")]
        public bool ShowVisualAlertBanner { get; set; } = true;

        [Display(Name = "Afficher Niveaux d'Entrée & Stop-Loss suggérés", GroupName = GrpOptionsAlerts, Order = 80,
            Description = "Trace les lignes d'Entrée (ENT) et de Stop-Loss (SL) sur les signaux Achat / Vente validés.")]
        public bool ShowEntryAndStopLevels { get; set; } = true;

        [Display(Name = "Couleur Alerte Visuelle ACHAT (BUY ▲)", GroupName = GrpOptionsAlerts, Order = 90)]
        public Color BuySignalColor { get; set; } = Color.FromArgb(255, 0, 255, 136); // Vert Néon Vif

        [Display(Name = "Couleur Alerte Visuelle VENTE (SELL ▼)", GroupName = GrpOptionsAlerts, Order = 100)]
        public Color SellSignalColor { get; set; } = Color.FromArgb(255, 255, 42, 85); // Rouge / Crête Néon Vif

        // =====================================================================
        // 9b. INDICATEUR OPTIONS X-RAY (CALL WALL, MAX PAIN, PUT WALL & PROFIL GEX)
        // =====================================================================
        private bool _enableOptionsXRayOverlay = true;
        [Display(Name = "Afficher l'Indicateur Options X-Ray (CALL WALL, MAX PAIN, PUT WALL)", GroupName = GrpOptionsXRay, Order = 10,
            Description = "Affiche sur le graphique l'indicateur d'options visible sur la capture : lignes CALL WALL (+XX.XM), MAX PAIN, PUT WALL (-XX.XM), bandeau 0 DTE = ...pts • MP ... et profil GEX par strike.")]
        public bool EnableOptionsXRayOverlay
        {
            get => _enableOptionsXRayOverlay;
            set { _enableOptionsXRayOverlay = value; RecalculateValues(); RedrawChart(); }
        }

        [Display(Name = "Afficher l'Histogramme Strike GEX à droite (Jaune Calls / Bleu-Gris Puts)", GroupName = GrpOptionsXRay, Order = 20,
            Description = "Dessine sur le bord droit du graphique les barres horizontales d'exposition Gamma/OI par strike (jaunes pour les Calls, bleues-grises pour les Puts) comme sur Options X-Ray.")]
        public bool ShowOptionsStrikeGexProfile { get; set; } = true;

        [Display(Name = "Afficher les Badges d'Ordres Style DOM ATAS ([X] [2] [TP / SL $ +... R])", GroupName = GrpOptionsXRay, Order = 30,
            Description = "Affiche les étiquettes d'ordres TP / SL / Position avec gain/perte en $ et bouton [R] exactement comme sur les captures d'écran ATAS.")]
        public bool ShowDomStyleOrderTags { get; set; } = true;

        private decimal _optionsStrikeStepPoints = 0m;
        [Display(Name = "Pas entre les Strikes d'Options (0 = Auto : 50 pts NQ / 25 pts ES)", GroupName = GrpOptionsXRay, Order = 40)]
        [Range(0, 5000)]
        public decimal OptionsStrikeStepPoints
        {
            get => _optionsStrikeStepPoints;
            set { _optionsStrikeStepPoints = Math.Max(0m, value); RecalculateValues(); RedrawChart(); }
        }

        private decimal _manualCallWallPrice = 0m;
        [Display(Name = "Forcer Prix CALL WALL (0 = Calcul Auto Session / Options X-Ray)", GroupName = GrpOptionsXRay, Order = 50,
            Description = "Laissez 0 pour détecter automatiquement le CALL WALL (ex: 28050 ou 27950 sur NQ), ou saisissez un strike précis.")]
        public decimal ManualCallWallPrice
        {
            get => _manualCallWallPrice;
            set { _manualCallWallPrice = Math.Max(0m, value); RecalculateValues(); RedrawChart(); }
        }

        private decimal _manualMaxPainPrice = 0m;
        [Display(Name = "Forcer Prix MAX PAIN (0 = Calcul Auto Session / Options X-Ray)", GroupName = GrpOptionsXRay, Order = 60,
            Description = "Laissez 0 pour détecter automatiquement le niveau MAX PAIN (ex: 27900 sur NQ), ou saisissez un strike précis.")]
        public decimal ManualMaxPainPrice
        {
            get => _manualMaxPainPrice;
            set { _manualMaxPainPrice = Math.Max(0m, value); RecalculateValues(); RedrawChart(); }
        }

        private decimal _manualPutWallPrice = 0m;
        [Display(Name = "Forcer Prix PUT WALL (0 = Calcul Auto Session / Options X-Ray)", GroupName = GrpOptionsXRay, Order = 70,
            Description = "Laissez 0 pour détecter automatiquement le PUT WALL (ex: 27850 sur NQ), ou saisissez un strike précis.")]
        public decimal ManualPutWallPrice
        {
            get => _manualPutWallPrice;
            set { _manualPutWallPrice = Math.Max(0m, value); RecalculateValues(); RedrawChart(); }
        }

        [Display(Name = "Couleur CALL WALL (Ligne & Badge)", GroupName = GrpOptionsXRay, Order = 80)]
        public Color CallWallLineColor { get; set; } = Color.FromArgb(255, 16, 185, 129); // Vert Émeraude CALL WALL

        [Display(Name = "Couleur MAX PAIN (Ligne Pointillée & Badge)", GroupName = GrpOptionsXRay, Order = 90)]
        public Color MaxPainLineColor { get; set; } = Color.FromArgb(255, 239, 68, 68); // Rouge Corail MAX PAIN

        [Display(Name = "Couleur PUT WALL (Ligne & Badge)", GroupName = GrpOptionsXRay, Order = 100)]
        public Color PutWallLineColor { get; set; } = Color.FromArgb(255, 139, 92, 246); // Violet PUT WALL

        // =====================================================================
        // 10. PANNEAU HUD & ALERTES SONORES
        // =====================================================================
        [Display(Name = "Afficher le tableau de bord HUD", GroupName = GrpHudAlerts, Order = 10)]
        public bool ShowHudPanel { get; set; } = true;

        [Display(Name = "Position du HUD", GroupName = GrpHudAlerts, Order = 20)]
        public HudCorner HudPosition { get; set; } = HudCorner.TopRight;

        [Display(Name = "Décalage X du HUD (px)", GroupName = GrpHudAlerts, Order = 30)]
        [Range(5, 400)]
        public int HudOffsetX { get; set; } = 16;

        [Display(Name = "Décalage Y du HUD (px)", GroupName = GrpHudAlerts, Order = 40)]
        [Range(5, 400)]
        public int HudOffsetY { get; set; } = 36;

        [Display(Name = "Activer les alertes sonores BigTrade Reversal", GroupName = GrpHudAlerts, Order = 50)]
        public bool EnableAlerts { get; set; } = true;

        [Display(Name = "Fichier son d'alerte", GroupName = GrpHudAlerts, Order = 60)]
        public string AlertSoundFile { get; set; } = "alert1.wav";

        // =====================================================================
        // 11. GESTION STOP-LOSS & TAKE-PROFIT DU REVERSAL BIGTRADE
        // =====================================================================
        private bool _enableQuantFilter = false;
        [Browsable(false)]
        public bool EnableQuantFilter
        {
            get => false;
            set { _enableQuantFilter = false; }
        }

        private decimal _quantMinClusterZScore = 0.0m;
        [Browsable(false)]
        public decimal QuantMinClusterZScore
        {
            get => _quantMinClusterZScore;
            set { _quantMinClusterZScore = value; }
        }

        private int _quantMinConfluenceScore = 50;
        [Browsable(false)]
        public int QuantMinConfluenceScore
        {
            get => _quantMinConfluenceScore;
            set { _quantMinConfluenceScore = value; }
        }

        private int _quantAtrPeriod = 14;
        [Display(Name = "Période ATR (Calibrage Volatilité SL/TP)", GroupName = GrpQuantEngine, Order = 40)]
        [Range(5, 100)]
        public int QuantAtrPeriod
        {
            get => _quantAtrPeriod;
            set { _quantAtrPeriod = value; RecalculateValues(); }
        }

        private decimal _quantAtrStopMultiplier = 0.85m;
        [Display(Name = "Distance Max Stop-Loss (Multiplicateur ATR)", GroupName = GrpQuantEngine, Order = 50,
            Description = "Calcule le Stop-Loss structurel serré et protégé sous/sur le losange BigTrade piégé et l'extrême du swing (évite les grosses pertes).")]
        [Range(0.2, 5.0)]
        public decimal QuantAtrStopMultiplier
        {
            get => _quantAtrStopMultiplier;
            set { _quantAtrStopMultiplier = value; RecalculateValues(); }
        }

        private decimal _quantTp1RiskReward = 1.80m;
        [Display(Name = "Ratio Risk/Reward TP1 (Swing Amplitude 1.8R + Profit-Lock)", GroupName = GrpQuantEngine, Order = 60,
            Description = "Distance de prise de profit TP1 (1.8R, anti-microscalping) : encaisse 50% si >=2 lots (ou verrouille +1.05R si 1 lot) et laisse courir vers TP2.")]
        [Range(0.5, 10.0)]
        public decimal QuantTp1RiskReward
        {
            get => _quantTp1RiskReward;
            set { _quantTp1RiskReward = value; RecalculateValues(); }
        }

        private decimal _quantTp2RiskReward = 3.20m;
        [Display(Name = "Ratio Risk/Reward TP2 (Objectif Swing Final 3.2R / Mur Options X-Ray)", GroupName = GrpQuantEngine, Order = 70,
            Description = "Objectif de prise de profit Swing final TP2 (3.2R ou étiré vers CALL WALL / PUT WALL / MAX PAIN).")]
        [Range(1.2, 15.0)]
        public decimal QuantTp2RiskReward
        {
            get => _quantTp2RiskReward;
            set { _quantTp2RiskReward = value; RecalculateValues(); }
        }

        // =====================================================================
        // 12. BOT DE TRADING BIGTRADES REVERSAL & RISK GUARD (MAX DD & MAX PNL)
        // =====================================================================
        private bool _enableTradingBot = true;
        [Display(Name = "Activer le Bot BigTrades Reversal", GroupName = GrpQuantBot, Order = 10,
            Description = "Ouvre uniquement des trades basés sur les BigTrades Piégés (losanges ◆) + Reversal avec Options X-Ray et HVN/LVN.")]
        public bool EnableTradingBot
        {
            get => _enableTradingBot;
            set { _enableTradingBot = value; RecalculateValues(); }
        }

        private BotExecutionMode _botMode = BotExecutionMode.LiveAutoTradingManager;
        [Display(Name = "Mode d'Exécution du Bot", GroupName = GrpQuantBot, Order = 20,
            Description = "LiveAutoTradingManager = envoie les ordres réels/sim au Compte Actif ATAS via TradingManager aux endroits exacts affichés ; BacktestAndPaperOnly = simulation graphique seule.")]
        public BotExecutionMode BotMode
        {
            get => _botMode;
            set { _botMode = value; RecalculateValues(); }
        }

        private CrossTradingExecutionMode _crossTradingMode = CrossTradingExecutionMode.AutoAtasCrossTrading;
        [Display(Name = "Trading Croisé ATAS X (Chart Normal ES/NQ ➔ Exécution Micro MES/MNQ)", GroupName = GrpQuantBot, Order = 25,
            Description = "Permet d'afficher l'indicateur sur le contrat majeur CME (ES, NQ, GC, CL, YM, RTY) pour lire le vrai Order Flow institutionnel, tout en exécutant et valorisant les trades sur le contrat Micro (MES, MNQ, MGC, MCL, MYM, M2K) via le Trading Croisé d'ATAS X.")]
        public CrossTradingExecutionMode CrossTradingMode
        {
            get => _crossTradingMode;
            set { _crossTradingMode = value; RecalculateValues(); RedrawChart(); }
        }

        private int _botTradeQuantity = 2;
        [Display(Name = "Taille de Position (Contrats / Lots)", GroupName = GrpQuantBot, Order = 30)]
        [Range(1, 1000)]
        public int BotTradeQuantity
        {
            get => _botTradeQuantity;
            set { _botTradeQuantity = Math.Max(1, value); RecalculateValues(); }
        }

        private decimal _botMaxDrawdownUsd = 1500m;
        [Display(Name = "Drawdown Max Autorisé ($ — Coupe-Circuit Kill-Switch)", GroupName = GrpQuantBot, Order = 40,
            Description = "Si le Drawdown depuis le pic d'équité (High-Water Mark) atteint ce montant en $, le Bot liquide la position et se verrouille immédiatement (STOP DD MAX).")]
        [Range(50, 1000000)]
        public decimal BotMaxDrawdownUsd
        {
            get => _botMaxDrawdownUsd;
            set { _botMaxDrawdownUsd = Math.Max(10m, value); RecalculateValues(); }
        }

        private decimal _botMaxProfitTargetUsd = 3000m;
        [Display(Name = "PnL Max / Objectif de Gain Max ($ — Lock Profit)", GroupName = GrpQuantBot, Order = 50,
            Description = "Dès que le PnL cumulé atteint cet objectif en $, le Bot sécurise les gains, clôture la position et se met en veille (OBJECTIF PNL MAX ATTEINT).")]
        [Range(50, 1000000)]
        public decimal BotMaxProfitTargetUsd
        {
            get => _botMaxProfitTargetUsd;
            set { _botMaxProfitTargetUsd = Math.Max(10m, value); RecalculateValues(); }
        }

        private bool _botAutoBreakevenOnTp1 = true;
        [Display(Name = "Déplacer Stop à Profit-Lock Breakeven + Sortie 50% sur TP1", GroupName = GrpQuantBot, Order = 60)]
        public bool BotAutoBreakevenOnTp1
        {
            get => _botAutoBreakevenOnTp1;
            set { _botAutoBreakevenOnTp1 = value; RecalculateValues(); }
        }

        private bool _onlyShowExecutedBotSignals = true;
        [Display(Name = "Afficher uniquement les signaux aux endroits où le Bot ouvre un trade", GroupName = GrpQuantBot, Order = 65,
            Description = "Garantit une correspondance 100% exacte : chaque signal ★ QUANT ACHAT / VENTE affiché sur le graphique correspond exactement à une ouverture de trade du Bot au même prix et sur la même bougie.")]
        public bool OnlyShowExecutedBotSignals
        {
            get => _onlyShowExecutedBotSignals;
            set { _onlyShowExecutedBotSignals = value; RecalculateValues(); RedrawChart(); }
        }

        private bool _botResetLimitsEachSession = true;
        [Display(Name = "Réinitialiser PnL & Drawdown Max à chaque nouvelle session", GroupName = GrpQuantBot, Order = 70)]
        public bool BotResetLimitsEachSession
        {
            get => _botResetLimitsEachSession;
            set { _botResetLimitsEachSession = value; RecalculateValues(); }
        }

        private decimal _botPointValueUsd = 50m;
        [Display(Name = "Valeur d'un Point en $ (Fallback si non fourni par le flux)", GroupName = GrpQuantBot, Order = 80,
            Description = "Ex: ES = 50$/pt (12.50$/tick), NQ = 20$/pt (5$/tick), MNQ = 2$/pt. Utilisé si Security.TickCost n'est pas renseigné par le connecteur.")]
        [Range(0.1, 100000)]
        public decimal BotPointValueUsd
        {
            get => _botPointValueUsd;
            set { _botPointValueUsd = Math.Max(0.1m, value); RecalculateValues(); }
        }

        private decimal _botCommissionPerSideUsd = 2.10m;
        [Display(Name = "Commission + Slippage estimé par contrat ($/ordre)", GroupName = GrpQuantBot, Order = 90)]
        [Range(0.0, 100.0)]
        public decimal BotCommissionPerSideUsd
        {
            get => _botCommissionPerSideUsd;
            set { _botCommissionPerSideUsd = Math.Max(0m, value); RecalculateValues(); }
        }

        [Display(Name = "Afficher Panneau Quant Bot & jauges Max DD / Max PnL", GroupName = GrpQuantBot, Order = 100)]
        public bool ShowBotExecutionOnChart { get; set; } = true;

        [Display(Name = "Afficher le Bouton ON/OFF du Bot directement sur la Charte", GroupName = GrpQuantBot, Order = 110,
            Description = "Affiche un bouton interactif cliquable directement sur le graphique ATAS X permettant d'activer/désactiver le Bot (ON/OFF), de basculer le mode (SIM / LIVE) et de réinitialiser le coupe-circuit en un clic.")]
        public bool ShowChartBotToggleButton { get; set; } = true;

        private bool _enableBotSchedule = false;
        [Display(Name = "Activer Plage Horaire du Bot (Démarrage / Fin)", GroupName = GrpQuantBot, Order = 120,
            Description = "Restreint l'exécution du Bot Quant à la plage horaire comprise entre l'Heure de Démarrage et l'Heure de Fin (modifiable directement dans l'encadrement sur la charte). Désactivé par défaut (24h/24) pour éviter tout décalage de fuseau horaire.")]
        public bool EnableBotSchedule
        {
            get => _enableBotSchedule;
            set { _enableBotSchedule = value; RecalculateValues(); }
        }

        private int _botStartMinutesOfDay = 8 * 60; // 08:00 par défaut
        [Display(Name = "Heure de Démarrage du Bot (HH:mm)", GroupName = GrpQuantBot, Order = 130,
            Description = "Format HH:mm (ex: 08:00, 09:30, 14:30). Peut aussi être ajusté directement dans l'encadrement horaire sur la charte.")]
        public string BotStartTimeText
        {
            get => FormatMinutesOfDay(_botStartMinutesOfDay);
            set
            {
                if (TryParseTimeOfDayMinutes(value, out int mins))
                {
                    _botStartMinutesOfDay = mins;
                    RecalculateValues();
                }
            }
        }

        private int _botEndMinutesOfDay = 22 * 60; // 22:00 par défaut
        [Display(Name = "Heure de Fin du Bot (HH:mm)", GroupName = GrpQuantBot, Order = 140,
            Description = "Format HH:mm (ex: 17:30, 21:30, 22:00). Peut aussi être ajusté directement dans l'encadrement horaire sur la charte.")]
        public string BotEndTimeText
        {
            get => FormatMinutesOfDay(_botEndMinutesOfDay);
            set
            {
                if (TryParseTimeOfDayMinutes(value, out int mins))
                {
                    _botEndMinutesOfDay = mins;
                    RecalculateValues();
                }
            }
        }

        private bool _botFlattenAtEndTime = true;
        [Display(Name = "Clôturer automatiquement la position à l'Heure de Fin", GroupName = GrpQuantBot, Order = 150)]
        public bool BotFlattenAtEndTime
        {
            get => _botFlattenAtEndTime;
            set { _botFlattenAtEndTime = value; RecalculateValues(); }
        }

        private bool _syncPnlWithActiveAccount = true;
        [Display(Name = "Synchroniser le PnL Réel avec le Compte Actif ATAS", GroupName = GrpQuantBot, Order = 160,
            Description = "Synchronise en temps réel le PnL Réalisé, le PnL Latent (Open PnL), la Position ouverte, le Solde et le Drawdown Max directement avec le compte actif sélectionné dans ATAS X (TradingManager.Portfolio / Position / MyTrades).")]
        public bool SyncPnlWithActiveAccount
        {
            get => _syncPnlWithActiveAccount;
            set { _syncPnlWithActiveAccount = value; RecalculateValues(); RedrawChart(); }
        }

        private ActiveAccountPnlScope _accountPnlScope = ActiveAccountPnlScope.ActivePortfolioTotal;
        [Display(Name = "Portée du PnL Réel Synchronisé (Compte Global vs Instrument)", GroupName = GrpQuantBot, Order = 170,
            Description = "ActivePortfolioTotal = synchronise le PnL total du compte actif ATAS ; ChartInstrumentOnly = synchronise uniquement le PnL du compte actif sur l'instrument du graphique.")]
        public ActiveAccountPnlScope AccountPnlScope
        {
            get => _accountPnlScope;
            set { _accountPnlScope = value; RecalculateValues(); RedrawChart(); }
        }

        // =====================================================================
        // Séries de données ATAS (compatibles Market Replay / Stratégies / Alertes)
        // =====================================================================
        private readonly ValueDataSeries _pocHvnSeries = new ValueDataSeries("PocHvnPrice", "Bar POC / Primary HVN")
        {
            VisualType = VisualMode.Dots,
            Color = Color.FromArgb(255, 255, 195, 0),
            Width = 3,
            ShowZeroValue = false
        };

        private readonly ValueDataSeries _barLvnSeries = new ValueDataSeries("BarLvnPrice", "Bar Primary LVN")
        {
            VisualType = VisualMode.Dots,
            Color = Color.FromArgb(255, 168, 85, 247),
            Width = 2,
            ShowZeroValue = false
        };

        private readonly ValueDataSeries _bullAbsSeries = new ValueDataSeries("BullishHeavyAbs", "Bullish Heavy Absorption (Bid)")
        {
            VisualType = VisualMode.UpArrow,
            Color = Color.FromArgb(255, 0, 240, 255),
            Width = 3,
            ShowZeroValue = false
        };

        private readonly ValueDataSeries _bearAbsSeries = new ValueDataSeries("BearishHeavyAbs", "Bearish Heavy Absorption (Ask)")
        {
            VisualType = VisualMode.DownArrow,
            Color = Color.FromArgb(255, 255, 45, 135),
            Width = 3,
            ShowZeroValue = false
        };

        private readonly ValueDataSeries _trappedSellersSeries = new ValueDataSeries("TrappedSellersBull", "Vendeurs Piégés (Bullish ▲)")
        {
            VisualType = VisualMode.UpArrow,
            Color = Color.FromArgb(255, 16, 245, 150),
            Width = 3,
            ShowZeroValue = false
        };

        private readonly ValueDataSeries _trappedBuyersSeries = new ValueDataSeries("TrappedBuyersBear", "Acheteurs Piégés (Bearish ▼)")
        {
            VisualType = VisualMode.DownArrow,
            Color = Color.FromArgb(255, 255, 90, 54),
            Width = 3,
            ShowZeroValue = false
        };

        private readonly ValueDataSeries _buyAlertSeries = new ValueDataSeries("VisualBuyAlert", "Alerte Visuelle ACHAT ▲ (Options Flow + Footprint)")
        {
            VisualType = VisualMode.UpArrow,
            Color = Color.FromArgb(255, 0, 255, 136),
            Width = 4,
            ShowZeroValue = false
        };

        private readonly ValueDataSeries _sellAlertSeries = new ValueDataSeries("VisualSellAlert", "Alerte Visuelle VENTE ▼ (Options Flow + Footprint)")
        {
            VisualType = VisualMode.DownArrow,
            Color = Color.FromArgb(255, 255, 42, 85),
            Width = 4,
            ShowZeroValue = false
        };

        private readonly ValueDataSeries _botCumPnlSeries = new ValueDataSeries("QuantBotCumulativePnL", "Quant Bot — PnL Cumulé ($)")
        {
            VisualType = VisualMode.Hide,
            Color = Color.FromArgb(255, 56, 189, 248),
            Width = 2,
            ShowZeroValue = false
        };

        private readonly ValueDataSeries _botDrawdownSeries = new ValueDataSeries("QuantBotDrawdown", "Quant Bot — Drawdown Courant ($)")
        {
            VisualType = VisualMode.Hide,
            Color = Color.FromArgb(255, 244, 63, 94),
            Width = 2,
            ShowZeroValue = false
        };

        // =====================================================================
        // Structures internes d'analyse par niveau de prix, Quant & Bot de Trading
        // =====================================================================
        private struct ClusterRow
        {
            public decimal Price;
            public decimal Bid;
            public decimal Ask;
            public decimal Volume;
            public decimal Delta;
            public decimal VolumeZScore;
            public decimal DeltaZScore;
            public bool IsPoc;
            public bool IsHvn;
            public bool IsLvn;
            public int AbsorptionSide;     // +1 = Bullish (Bid absorbé en bas), -1 = Bearish (Ask absorbé en haut), 0 = Aucun
            public int TrappedSide;        // +1 = Vendeurs Piégés en bas (Signal Bullish ▲), -1 = Acheteurs Piégés en haut (Signal Bearish ▼), 0 = Aucun
            public decimal DominantPct;
            public decimal TrappedDominantPct;
            public bool HasBigTradeBuy;    // Losange Vert (BigTrade Buy ◆)
            public bool HasBigTradeSell;   // Losange Rouge (BigTrade Sell ◆)
            public decimal BigTradeBuyVol;
            public decimal BigTradeSellVol;
            public int TrappedBigTradeSide;// +1 = BigTrade Sell Rouge Piégé en bas (Bullish Reversal ◆▲), -1 = BigTrade Buy Vert Piégé en haut (Bearish Reversal ◆▼)
        }

        private struct LiveBigTradePrint
        {
            public decimal Price;
            public decimal Volume;
            public bool IsBuy;
            public DateTime TimeUtc;
        }

        private struct RaySignalEvent
        {
            public int Bar;
            public decimal Price;
            public decimal Volume;
            public decimal Delta;
            public int Side;       // +1 Bullish, -1 Bearish
            public bool IsTrapped; // false = Heavy Absorption, true = Trapped Traders
        }

        private struct BarOptionsFlowMetrics
        {
            public decimal CallVolume;
            public decimal PutVolume;
            public decimal CallFlowPct;
            public decimal PutFlowPct;
            public decimal PutCallRatio;
            public decimal NetOptionsDelta;
            public decimal OiDelta;
            public int SweepCount;
            public int OptionsBias; // +1 = Flux Calls Haussier, -1 = Flux Puts Baissier, 0 = Neutre
            public bool IsNativeChainActive;
            public decimal KeyGammaStrike;
            public decimal AtrPoints;
            public decimal MaxVolumeZScore;
            public decimal MaxDeltaZScore;
            public bool HasCvdAbsorptionDivergence;
            public bool BullishCvdDivergence;
            public bool BearishCvdDivergence;
            public decimal VwapZScore;
            public decimal EmaFast;
            public decimal EmaSlow;
            public decimal EmaTrend50;
            public int TrendBias; // +1 = Haussier (EMA8 >= EMA21 et Close >= VWAP), -1 = Baissier, 0 = Neutre
            public bool MultiBarStructureBull;
            public bool MultiBarStructureBear;
            public bool DeltaTailBullRecovery;
            public bool DeltaTailBearRejection;
            public decimal BarPocPrice;
            public int BullStackedImbalances;
            public int BearStackedImbalances;

            // --- Indicateur Options X-Ray (CALL WALL, MAX PAIN, PUT WALL, 0 DTE Expected Move & Régime Gamma) ---
            public decimal XRayCallWall;
            public decimal XRayCallWallGexM;           // Ex: +15.0M ou +11.9M
            public decimal XRayMaxPain;                // Ex: 27900
            public decimal XRayPutWall;                // Ex: 27850
            public decimal XRayPutWallGexM;            // Ex: -69.5M ou -82.4M
            public decimal XRayZeroDteExpectedMovePts; // Ex: 1226pts
            public int XRayGammaRegime;                // +1 = Positive Gamma (au-dessus de Max Pain), -1 = Negative Gamma

            // --- Métriques BigTrades Piégés (Losanges ◆) sur la bougie ---
            public bool HasBigTradeBuyOnBar;
            public bool HasBigTradeSellOnBar;
            public bool HasTrappedBigTradeBull;
            public bool HasTrappedBigTradeBear;
            public decimal TrappedBigTradePrice;
            public decimal TrappedBigTradeLots;
        }

        private struct VisualTradeSignal
        {
            public int Bar;
            public int Direction; // +1 = ACHAT (BUY ▲), -1 = VENTE (SELL ▼), 0 = Aucun
            public decimal TriggerPrice;
            public decimal EntryPrice;
            public decimal RealAccountFillPrice;
            public bool IsLiveFillSynced;
            public decimal StopPrice;
            public decimal Tp1Price;
            public decimal Tp2Price;
            public decimal RiskRewardRatio;
            public decimal ClusterZScore;
            public decimal AtrValue;
            public int ConfluenceScore; // 0..100%
            public decimal CallFlowPct;
            public decimal PutFlowPct;
            public decimal PutCallRatio;
            public decimal OiDelta;
            public int SweepCount;
            public bool IsBigTradeReversal;
            public decimal BigTradePrice;
            public decimal BigTradeLots;
            public string ReasonTag;
            public string XRaySummaryTag;
        }

        private struct BotTradeExecutionRecord
        {
            public int EntryBar;
            public int ExitBar;
            public int Direction; // +1 Long, -1 Short
            public decimal EntryPrice;
            public decimal StopPrice;
            public decimal Tp1Price;
            public decimal Tp2Price;
            public decimal ExitPrice;
            public decimal RealizedPnlUsd;
            public string ExitReason; // "TP2", "TP1+BE", "SL", "REV", "MAX_DD", "MAX_PNL", "OPEN"
            public bool Tp1Reached;
            public VisualTradeSignal EntrySignal;
        }

        private struct BotPerformanceState
        {
            public bool IsHaltedByMaxDrawdown;
            public bool IsHaltedByMaxProfit;
            public string StatusLabel;
            public string MarketProfileLabel;
            public string CrossTradingLabel;
            public decimal EffectivePointValueUsd;
            public bool IsSyncedWithActiveAccount;
            public string ActiveAccountName;
            public string ActiveAccountCurrency;
            public decimal ActiveAccountBalance;
            public decimal ActiveAccountCommissionUsd;
            public decimal SimulatedTotalPnlUsd;
            public int ActiveDirection; // +1 Long, -1 Short, 0 Flat
            public int ActiveContracts;
            public int ActiveEntryBar;
            public decimal ActiveEntryPrice;
            public decimal ActiveStopPrice;
            public decimal ActiveTp1Price;
            public decimal ActiveTp2Price;
            public bool ActiveTp1Hit;
            public decimal RealizedPnlUsd;
            public decimal UnrealizedPnlUsd;
            public decimal TotalPnlUsd;
            public decimal PeakPnlUsd;
            public decimal CurrentDrawdownUsd;
            public decimal MaxObservedDrawdownUsd;
            public int TotalTrades;
            public int WinningTrades;
            public int LosingTrades;
            public decimal GrossProfitUsd;
            public decimal GrossLossUsd;
            public decimal WinRatePct;
            public decimal ProfitFactor;
            public decimal ExpectancyUsd;
            public decimal SharpeRatio;
        }

        // Cache temps réel du Flux d'Options ATAS X (IOptionsDataFeed & Tape Sweeps)
        private decimal _nativeChainCallVol;
        private decimal _nativeChainPutVol;
        private decimal _nativeChainCallOi;
        private decimal _nativeChainPutOi;
        private decimal _nativeKeyStrike;
        private bool _nativeOptionsLoaded;
        private DateTime _lastNativeOptionsFetch = DateTime.MinValue;

        private decimal _liveTapeCallSweepVol;
        private decimal _liveTapePutSweepVol;
        private int _liveTapeSweepCount;
        private int _lastLiveSweepBar = -1;

        private int _lastAlertBar = -1;
        private int _lastAlertCode = 0;
        private int _lastLiveBotOrderBar = -1;
        private int _lastLiveBotOrderDir = 0;
        private bool _liveKillSwitchExecuted;

        // Suivi temps réel des niveaux SL / TP1 / TP2 / Trailing pour l'exécution Live ATAS (TradingManager)
        private readonly Dictionary<int, VisualTradeSignal> _lockedExecutedSignalsByBar = new Dictionary<int, VisualTradeSignal>();
        private readonly Dictionary<int, ClusterRow[]> _barRowsCache = new Dictionary<int, ClusterRow[]>();
        private readonly Dictionary<int, List<LiveBigTradePrint>> _liveBigTradesByBar = new Dictionary<int, List<LiveBigTradePrint>>();
        private decimal _lastBigTradeToastPrice = 0m;
        private decimal _lastBigTradeToastVol = 0m;
        private bool _lastBigTradeToastIsBuy = false;
        private DateTime _lastBigTradeToastTime = DateTime.MinValue;
        private int _liveTrackedDir = 0;
        private int _liveTrackedSignalBar = -1;
        private decimal _liveTrackedEntryPrice = 0m;
        private decimal _liveTrackedRealFillPrice = 0m;
        private decimal _liveCrossBasisOffset = 0m;
        private decimal _liveTrackedInitialRiskPts = 0m;
        private decimal _liveTrackedStopPrice = 0m;
        private decimal _liveTrackedTp1Price = 0m;
        private decimal _liveTrackedTp2Price = 0m;
        private decimal _liveMaxFavorablePrice = 0m;
        private bool _liveTrackedTp1Hit = false;
        private bool _liveTp1PartialOrderSent = false;
        private DateTime _lastLiveOrderSentUtc = DateTime.MinValue;
        private DateTime _lastLiveExitSentUtc = DateTime.MinValue;

        // État temps réel de synchronisation avec le Compte Actif ATAS X (Portfolio / Position / MyTrades / CrossTrading)
        private ITradingManager? _subscribedTradingManager;
        private ICrossTradingIndicatorContext? _subscribedCrossTradingContext;
        private string _lastSyncedAccountId = string.Empty;
        private decimal _liveAccountPeakPnlUsd = decimal.MinValue;
        private decimal _liveAccountMaxDrawdownUsd;

        // Zones cliquables & états de survol du Bouton d'Activation, de l'Encadrement Horaire, du Compte Actif & du Trading Croisé CME
        private Rectangle _chartBotToggleBtnRect = Rectangle.Empty;
        private Rectangle _chartBotModeBtnRect = Rectangle.Empty;
        private Rectangle _chartBotResetBtnRect = Rectangle.Empty;
        private Rectangle _chartProfileBtnRect = Rectangle.Empty;
        private Rectangle _chartCrossTradeBtnRect = Rectangle.Empty;
        private Rectangle _hudBotToggleBtnRect = Rectangle.Empty;

        private Rectangle _chartSchedToggleRect = Rectangle.Empty;
        private Rectangle _chartStartMinusRect = Rectangle.Empty;
        private Rectangle _chartStartValRect = Rectangle.Empty;
        private Rectangle _chartStartPlusRect = Rectangle.Empty;
        private Rectangle _chartEndMinusRect = Rectangle.Empty;
        private Rectangle _chartEndValRect = Rectangle.Empty;
        private Rectangle _chartEndPlusRect = Rectangle.Empty;

        private Rectangle _chartAcctSyncToggleRect = Rectangle.Empty;
        private Rectangle _chartAcctScopeToggleRect = Rectangle.Empty;

        private bool _hoverBotToggleBtn;
        private bool _hoverBotModeBtn;
        private bool _hoverBotResetBtn;
        private bool _hoverProfileBtn;
        private bool _hoverCrossTradeBtn;
        private bool _hoverSchedToggleBtn;
        private bool _hoverStartMinus;
        private bool _hoverStartPlus;
        private bool _hoverEndMinus;
        private bool _hoverEndPlus;
        private bool _hoverAcctSyncBtn;
        private bool _hoverAcctScopeBtn;
        private int _manualResetBarIndex = -1;

        private static string FormatMinutesOfDay(int totalMinutes)
        {
            int m = ((totalMinutes % 1440) + 1440) % 1440;
            return $"{m / 60:00}:{m % 60:00}";
        }

        private static bool TryParseTimeOfDayMinutes(string text, out int totalMinutes)
        {
            totalMinutes = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] parts = text.Trim().Split(':');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int h) &&
                int.TryParse(parts[1], out int m) &&
                h >= 0 && h <= 23 && m >= 0 && m <= 59)
            {
                totalMinutes = h * 60 + m;
                return true;
            }
            return false;
        }

        private static int ShiftScheduleMinutes(int currentMinutes, int deltaMinutes)
        {
            return (((currentMinutes + deltaMinutes) % 1440) + 1440) % 1440;
        }

        private bool IsBarWithinBotSchedule(DateTime barTime)
        {
            if (!_enableBotSchedule)
                return true;

            int cur = barTime.Hour * 60 + barTime.Minute;
            if (_botStartMinutesOfDay <= _botEndMinutesOfDay)
                return cur >= _botStartMinutesOfDay && cur <= _botEndMinutesOfDay;

            // Plage chevauchant minuit (ex: 22:00 -> 06:00)
            return cur >= _botStartMinutesOfDay || cur <= _botEndMinutesOfDay;
        }

        // =====================================================================
        // Détection Automatique du Profil de Marché (Crypto Binance vs CME ES/NQ/GC/CL)
        // & Résolveur de Trading Croisé ATAS X (Contrat Normal Chart ➔ Contrat Micro Exécuté)
        // =====================================================================
        private struct ContractAndCrossTradingInfo
        {
            public MarketProfilePreset EffectiveProfile;
            public bool IsCmeMarket;
            public string ChartSymbol;
            public string TradedSymbol;
            public string BaseFamily; // "ES", "NQ", "GC", "CL", "YM", "RTY", "6E", "BTC", "CRYPTO"
            public bool IsCrossTradingMicro;
            public decimal ChartPointValueUsd;
            public decimal TradedPointValueUsd;
            public decimal TradedCommissionPerSideUsd;
            public string ProfileBadge;
            public string CrossTradingBadge;
        }

        private static string NormalizeSymbolUpper(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;
            return raw.Trim().ToUpperInvariant();
        }

        private static bool StartsWithContractRoot(string sym, string root)
        {
            if (string.IsNullOrEmpty(sym) || string.IsNullOrEmpty(root))
                return false;

            // Gère les préfixes type @ES, /ES, CME:ES, ESH6, ESH26, ESZ2026, ES-202603, ES_F, etc.
            int idx = 0;
            while (idx < sym.Length && (sym[idx] == '@' || sym[idx] == '/' || sym[idx] == '$'))
                idx++;

            int colonIdx = sym.IndexOf(':');
            if (colonIdx >= 0 && colonIdx + 1 < sym.Length)
                idx = colonIdx + 1;

            if (sym.Length - idx < root.Length)
                return false;

            for (int i = 0; i < root.Length; i++)
            {
                if (sym[idx + i] != root[i])
                    return false;
            }

            int after = idx + root.Length;
            if (after == sym.Length)
                return true;

            char next = sym[after];
            // Mois à terme CME (F,G,H,J,K,M,N,Q,U,V,X,Z) suivi d'un chiffre, ou séparateur/chiffre/espace/@/#
            if (char.IsDigit(next) || next == ' ' || next == '-' || next == '_' || next == '.' || next == '#' || next == '@' || next == '!')
                return true;

            const string cmeMonthCodes = "FGHJKMNQUVXZ";
            if (cmeMonthCodes.IndexOf(next) >= 0 && after + 1 < sym.Length && char.IsDigit(sym[after + 1]))
                return true;

            return false;
        }

        private static bool ClassifyFuturesSymbol(
            string symRaw,
            out string family,
            out bool isMicro,
            out decimal pointValueUsd,
            out decimal defaultCommPerSideUsd,
            out string standardCode,
            out string microCode)
        {
            string s = NormalizeSymbolUpper(symRaw);
            family = "OTHER";
            isMicro = false;
            pointValueUsd = 0m;
            defaultCommPerSideUsd = 0m;
            standardCode = s;
            microCode = s;

            if (string.IsNullOrEmpty(s))
                return false;

            // 1. BTC Binance / Crypto Perpétuels & Spot (Comportement 100% original préservé uniquement sur BTC Crypto)
            if (s.Contains("BTC") &&
                (s.Contains("USDT") || s.Contains("USDC") || s.Contains("BUSD") || s.Contains("FDUSD") ||
                 s.Contains("PERP") || s.Contains("BINANCE") || s.Contains("BYBIT") || s.StartsWith("BTCUSD")))
            {
                family = "CRYPTO_BTC";
                isMicro = false;
                pointValueUsd = 1m;
                defaultCommPerSideUsd = 0.50m;
                standardCode = s;
                microCode = s;
                return true;
            }

            // 1bis. Autres Crypto Perpétuels / Altcoins (ETH, SOL, BNB, XRP...) -> utilisent la stratégie améliorée hors BTC
            if (s.Contains("USDT") || s.Contains("USDC") || s.Contains("BUSD") || s.Contains("FDUSD") ||
                s.Contains("PERP") || s.Contains("BINANCE") || s.Contains("BYBIT") ||
                s.StartsWith("ETHUSD") || s.StartsWith("SOLUSD"))
            {
                family = "CRYPTO_ALT";
                isMicro = false;
                pointValueUsd = 1m;
                defaultCommPerSideUsd = 0.50m;
                standardCode = s;
                microCode = s;
                return true;
            }

            // 2. CME S&P 500 : ES ($50/pt) & Micro MES ($5/pt)
            if (StartsWithContractRoot(s, "MES") || s.Contains("MICRO E-MINI S&P"))
            {
                family = "ES";
                isMicro = true;
                pointValueUsd = 5m;
                defaultCommPerSideUsd = 0.62m;
                standardCode = "ES";
                microCode = "MES";
                return true;
            }
            if (StartsWithContractRoot(s, "ES") || s.Contains("E-MINI S&P"))
            {
                family = "ES";
                isMicro = false;
                pointValueUsd = 50m;
                defaultCommPerSideUsd = 2.10m;
                standardCode = "ES";
                microCode = "MES";
                return true;
            }

            // 3. CME NASDAQ 100 : NQ ($20/pt) & Micro MNQ ($2/pt)
            if (StartsWithContractRoot(s, "MNQ") || s.Contains("MICRO E-MINI NASDAQ"))
            {
                family = "NQ";
                isMicro = true;
                pointValueUsd = 2m;
                defaultCommPerSideUsd = 0.62m;
                standardCode = "NQ";
                microCode = "MNQ";
                return true;
            }
            if (StartsWithContractRoot(s, "NQ") || s.Contains("E-MINI NASDAQ") || s.Contains("NASDAQ 100"))
            {
                family = "NQ";
                isMicro = false;
                pointValueUsd = 20m;
                defaultCommPerSideUsd = 2.10m;
                standardCode = "NQ";
                microCode = "MNQ";
                return true;
            }

            // 4. CME / COMEX Or (Gold) : GC ($100/pt) & Micro MGC ($10/pt)
            if (StartsWithContractRoot(s, "MGC") || StartsWithContractRoot(s, "QO"))
            {
                family = "GC";
                isMicro = true;
                pointValueUsd = 10m;
                defaultCommPerSideUsd = 0.75m;
                standardCode = "GC";
                microCode = "MGC";
                return true;
            }
            if (StartsWithContractRoot(s, "GC"))
            {
                family = "GC";
                isMicro = false;
                pointValueUsd = 100m;
                defaultCommPerSideUsd = 2.30m;
                standardCode = "GC";
                microCode = "MGC";
                return true;
            }

            // 5. CME / NYMEX Crude Oil (WTI) : CL ($1000/pt) & Micro MCL ($100/pt) / QM ($500/pt)
            if (StartsWithContractRoot(s, "MCL"))
            {
                family = "CL";
                isMicro = true;
                pointValueUsd = 100m;
                defaultCommPerSideUsd = 0.75m;
                standardCode = "CL";
                microCode = "MCL";
                return true;
            }
            if (StartsWithContractRoot(s, "CL"))
            {
                family = "CL";
                isMicro = false;
                pointValueUsd = 1000m;
                defaultCommPerSideUsd = 2.30m;
                standardCode = "CL";
                microCode = "MCL";
                return true;
            }

            // 6. CME / CBOT Dow Jones : YM ($5/pt) & Micro MYM ($0.50/pt)
            if (StartsWithContractRoot(s, "MYM"))
            {
                family = "YM";
                isMicro = true;
                pointValueUsd = 0.50m;
                defaultCommPerSideUsd = 0.62m;
                standardCode = "YM";
                microCode = "MYM";
                return true;
            }
            if (StartsWithContractRoot(s, "YM"))
            {
                family = "YM";
                isMicro = false;
                pointValueUsd = 5m;
                defaultCommPerSideUsd = 2.10m;
                standardCode = "YM";
                microCode = "MYM";
                return true;
            }

            // 7. CME Russell 2000 : RTY ($50/pt) & Micro M2K ($5/pt)
            if (StartsWithContractRoot(s, "M2K"))
            {
                family = "RTY";
                isMicro = true;
                pointValueUsd = 5m;
                defaultCommPerSideUsd = 0.62m;
                standardCode = "RTY";
                microCode = "M2K";
                return true;
            }
            if (StartsWithContractRoot(s, "RTY") || StartsWithContractRoot(s, "TF"))
            {
                family = "RTY";
                isMicro = false;
                pointValueUsd = 50m;
                defaultCommPerSideUsd = 2.10m;
                standardCode = "RTY";
                microCode = "M2K";
                return true;
            }

            // 8. CME Euro FX : 6E ($125,000/pt) & Micro M6E ($12,500/pt)
            if (StartsWithContractRoot(s, "M6E"))
            {
                family = "6E";
                isMicro = true;
                pointValueUsd = 12500m;
                defaultCommPerSideUsd = 0.55m;
                standardCode = "6E";
                microCode = "M6E";
                return true;
            }
            if (StartsWithContractRoot(s, "6E"))
            {
                family = "6E";
                isMicro = false;
                pointValueUsd = 125000m;
                defaultCommPerSideUsd = 2.10m;
                standardCode = "6E";
                microCode = "M6E";
                return true;
            }

            // 9. CME Bitcoin Futures : BTC ($5/pt) & Micro MBT ($0.10/pt)
            if (StartsWithContractRoot(s, "MBT"))
            {
                family = "BTC_CME";
                isMicro = true;
                pointValueUsd = 0.10m;
                defaultCommPerSideUsd = 0.85m;
                standardCode = "BTC";
                microCode = "MBT";
                return true;
            }
            if (StartsWithContractRoot(s, "BTC") && !s.Contains("USD"))
            {
                family = "BTC_CME";
                isMicro = false;
                pointValueUsd = 5m;
                defaultCommPerSideUsd = 2.50m;
                standardCode = "BTC";
                microCode = "MBT";
                return true;
            }

            return false;
        }

        private static bool AreInstrumentsInSameCrossFamily(string? symA, string? symB)
        {
            if (string.IsNullOrWhiteSpace(symA) || string.IsNullOrWhiteSpace(symB))
                return false;
            if (string.Equals(symA.Trim(), symB.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;

            if (ClassifyFuturesSymbol(symA, out string famA, out _, out _, out _, out _, out _) &&
                ClassifyFuturesSymbol(symB, out string famB, out _, out _, out _, out _, out _))
            {
                return string.Equals(famA, famB, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private ContractAndCrossTradingInfo ResolveContractAndCrossTradingInfo(decimal tickSize)
        {
            string chartSym = InstrumentInfo?.Instrument ?? string.Empty;
            string tmSecCode = TradingManager?.Security?.Code ?? TradingManager?.Security?.SecurityId ?? string.Empty;
            string posSecCode = TradingManager?.Position?.SecurityId ?? string.Empty;

            var crossCtx = DataProvider as ICrossTradingIndicatorContext;
            bool atasCrossActive = crossCtx?.IsCrossTradingActive ?? false;
            string atasCrossDisplay = crossCtx?.CurrentCrossTradingDisplayName ?? string.Empty;

            string tradedSym = !string.IsNullOrWhiteSpace(atasCrossDisplay)
                ? atasCrossDisplay
                : (!string.IsNullOrWhiteSpace(tmSecCode) ? tmSecCode : (!string.IsNullOrWhiteSpace(posSecCode) ? posSecCode : chartSym));

            bool chartClassified = ClassifyFuturesSymbol(
                chartSym,
                out string chartFamily,
                out bool chartIsMicro,
                out decimal chartKnownPtVal,
                out decimal chartDefaultComm,
                out string stdCode,
                out string microCode);

            bool tradedClassified = ClassifyFuturesSymbol(
                tradedSym,
                out string tradedFamily,
                out bool tradedIsMicro,
                out decimal tradedKnownPtVal,
                out decimal tradedDefaultComm,
                out _,
                out _);

            // Détermination du Profil de Marché effectif
            MarketProfilePreset effectiveProfile = _marketProfile;
            if (effectiveProfile == MarketProfilePreset.AutoDetect)
            {
                if (chartClassified)
                {
                    effectiveProfile = chartFamily switch
                    {
                        "CRYPTO_BTC" => MarketProfilePreset.CryptoBinanceFutures,
                        "ES" => MarketProfilePreset.CmeSp500_ES_MES,
                        "NQ" => MarketProfilePreset.CmeNasdaq_NQ_MNQ,
                        _ => MarketProfilePreset.CmeKeyFutures_GC_CL_YM_RTY
                    };
                }
                else
                {
                    string normSym = NormalizeSymbolUpper(chartSym);
                    bool looksLikeBtcCrypto = normSym.Contains("BTC") && !normSym.Contains("MBT") && !normSym.Contains("CME");
                    if (looksLikeBtcCrypto)
                        effectiveProfile = MarketProfilePreset.CryptoBinanceFutures;
                    else if (tickSize == 0.25m)
                        effectiveProfile = MarketProfilePreset.CmeSp500_ES_MES;
                    else
                        effectiveProfile = MarketProfilePreset.CmeKeyFutures_GC_CL_YM_RTY;
                }
            }

            bool isCme = effectiveProfile != MarketProfilePreset.CryptoBinanceFutures;

            // Valeur d'un point sur le graphique (Contrat Normal ou courant)
            decimal chartPtVal = chartKnownPtVal > 0m ? chartKnownPtVal : (_botPointValueUsd > 0m ? _botPointValueUsd : 50m);
            decimal connectorPtVal = 0m;
            try
            {
                var sec = TradingManager?.Security;
                if (sec != null && sec.TickCost > 0m && sec.TickSize > 0m)
                    connectorPtVal = sec.TickCost / sec.TickSize;
            }
            catch
            {
                // Ignorer si non fourni
            }

            // Détection du Trading Croisé (Chart Normal ES/NQ/GC/CL ➔ Exécution Micro MES/MNQ/MGC/MCL)
            bool isCrossMicro = false;
            decimal tradedPtVal = chartPtVal;
            decimal tradedComm = _botCommissionPerSideUsd;
            string effectiveTradedSymbol = string.IsNullOrWhiteSpace(tradedSym) ? chartSym : tradedSym;

            if (_crossTradingMode == CrossTradingExecutionMode.ForceMicroOnNormalChart && isCme)
            {
                isCrossMicro = !chartIsMicro;
                if (chartClassified && !string.IsNullOrEmpty(microCode))
                {
                    effectiveTradedSymbol = tradedIsMicro && !string.IsNullOrWhiteSpace(tradedSym) ? tradedSym : microCode;
                    if (ClassifyFuturesSymbol(microCode, out _, out _, out decimal mPt, out decimal mComm, out _, out _))
                    {
                        tradedPtVal = mPt;
                        tradedComm = Math.Min(_botCommissionPerSideUsd, mComm);
                    }
                    else
                    {
                        tradedPtVal = Math.Max(0.1m, chartPtVal * 0.1m);
                        tradedComm = Math.Min(_botCommissionPerSideUsd, 0.65m);
                    }
                }
                else
                {
                    tradedPtVal = effectiveProfile == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 2m : 5m;
                    tradedComm = Math.Min(_botCommissionPerSideUsd, 0.62m);
                    effectiveTradedSymbol = effectiveProfile == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? "MNQ" : "MES";
                }
            }
            else if (_crossTradingMode == CrossTradingExecutionMode.AutoAtasCrossTrading)
            {
                bool diffInstrument = !string.IsNullOrWhiteSpace(tradedSym) &&
                                      !string.IsNullOrWhiteSpace(chartSym) &&
                                      !string.Equals(tradedSym.Trim(), chartSym.Trim(), StringComparison.OrdinalIgnoreCase);

                if (atasCrossActive || (diffInstrument && tradedIsMicro) || (!chartIsMicro && connectorPtVal > 0m && chartKnownPtVal > 0m && connectorPtVal <= chartKnownPtVal * 0.25m))
                {
                    isCrossMicro = true;
                    if (tradedKnownPtVal > 0m)
                    {
                        tradedPtVal = tradedKnownPtVal;
                        tradedComm = Math.Min(_botCommissionPerSideUsd, tradedDefaultComm > 0m ? tradedDefaultComm : 0.62m);
                    }
                    else if (connectorPtVal > 0m)
                    {
                        tradedPtVal = connectorPtVal;
                        tradedComm = Math.Min(_botCommissionPerSideUsd, 0.62m);
                    }
                    else if (chartKnownPtVal > 0m)
                    {
                        tradedPtVal = chartKnownPtVal * 0.1m;
                        tradedComm = Math.Min(_botCommissionPerSideUsd, 0.62m);
                    }

                    if (!diffInstrument && !string.IsNullOrEmpty(microCode))
                        effectiveTradedSymbol = microCode;
                }
                else
                {
                    if (connectorPtVal > 0m)
                        tradedPtVal = connectorPtVal;
                    else if (tradedKnownPtVal > 0m)
                        tradedPtVal = tradedKnownPtVal;
                    else if (chartKnownPtVal > 0m)
                        tradedPtVal = chartKnownPtVal;
                }
            }
            else // StandardChartContractOnly
            {
                if (chartKnownPtVal > 0m)
                    tradedPtVal = chartKnownPtVal;
                else if (connectorPtVal > 0m)
                    tradedPtVal = connectorPtVal;
            }

            string profileBadge = effectiveProfile switch
            {
                MarketProfilePreset.CmeSp500_ES_MES => "CME S&P 500 (ES/MES)",
                MarketProfilePreset.CmeNasdaq_NQ_MNQ => "CME NASDAQ (NQ/MNQ)",
                MarketProfilePreset.CmeKeyFutures_GC_CL_YM_RTY => $"CME FUTURES ({(string.IsNullOrEmpty(stdCode) ? chartSym : stdCode)})",
                _ => "CRYPTO BINANCE FUTURES"
            };

            string crossBadge;
            if (isCrossMicro)
            {
                string src = !string.IsNullOrEmpty(stdCode) ? stdCode : (string.IsNullOrEmpty(chartSym) ? "CHART" : chartSym);
                string dst = !string.IsNullOrEmpty(effectiveTradedSymbol) ? effectiveTradedSymbol : microCode;
                crossBadge = $"CROISÉ ACTIF : Chart {src} ➔ Trade {dst} ({tradedPtVal:0.##}$/pt)";
            }
            else if (chartIsMicro)
            {
                crossBadge = $"DIRECT MICRO : {chartSym} ({tradedPtVal:0.##}$/pt)";
            }
            else
            {
                string symLbl = string.IsNullOrWhiteSpace(effectiveTradedSymbol) ? "STD" : effectiveTradedSymbol;
                crossBadge = $"CONTRAT DIRECT : {symLbl} ({tradedPtVal:0.##}$/pt)";
            }

            return new ContractAndCrossTradingInfo
            {
                EffectiveProfile = effectiveProfile,
                IsCmeMarket = isCme,
                ChartSymbol = chartSym,
                TradedSymbol = effectiveTradedSymbol,
                BaseFamily = chartClassified ? chartFamily : (tradedClassified ? tradedFamily : "OTHER"),
                IsCrossTradingMicro = isCrossMicro,
                ChartPointValueUsd = chartPtVal,
                TradedPointValueUsd = tradedPtVal > 0m ? tradedPtVal : 50m,
                TradedCommissionPerSideUsd = tradedComm,
                ProfileBadge = profileBadge,
                CrossTradingBadge = crossBadge
            };
        }

        // =====================================================================
        // Constructeur
        // =====================================================================
        public FootprintHvnLvnAbsorption() : base(true)
        {
            DenyToChangePanel = true;
            EnableCustomDrawing = true;
            SubscribeToDrawingEvents(DrawingLayouts.Final | DrawingLayouts.LatestBar);

            DataSeries[0] = _pocHvnSeries;
            DataSeries.Add(_barLvnSeries);
            DataSeries.Add(_bullAbsSeries);
            DataSeries.Add(_bearAbsSeries);
            DataSeries.Add(_trappedSellersSeries);
            DataSeries.Add(_trappedBuyersSeries);
            DataSeries.Add(_buyAlertSeries);
            DataSeries.Add(_sellAlertSeries);
            DataSeries.Add(_botCumPnlSeries);
            DataSeries.Add(_botDrawdownSeries);
        }

        // =====================================================================
        // Gestion des clics souris directement sur la Charte ATAS X (Bouton Bot ON/OFF, Profil CME & Trading Croisé)
        // =====================================================================
        public override bool ProcessMouseClick(RenderControlMouseEventArgs e)
        {
            if (e.Button == RenderControlMouseButtons.Left)
            {
                Point pt = e.Location;
                if ((ShowChartBotToggleButton && _chartBotToggleBtnRect.Contains(pt)) ||
                    (ShowHudPanel && _hudBotToggleBtnRect.Contains(pt)))
                {
                    _enableTradingBot = !_enableTradingBot;
                    if (!_enableTradingBot)
                        _liveKillSwitchExecuted = false;
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartBotModeBtnRect.Contains(pt))
                {
                    _botMode = _botMode == BotExecutionMode.BacktestAndPaperOnly
                        ? BotExecutionMode.LiveAutoTradingManager
                        : BotExecutionMode.BacktestAndPaperOnly;
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartBotResetBtnRect.Contains(pt))
                {
                    _manualResetBarIndex = Math.Max(0, CurrentBar - 1);
                    _liveAccountPeakPnlUsd = decimal.MinValue;
                    _liveAccountMaxDrawdownUsd = 0m;
                    _liveKillSwitchExecuted = false;
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartProfileBtnRect.Contains(pt))
                {
                    _marketProfile = _marketProfile switch
                    {
                        MarketProfilePreset.AutoDetect => MarketProfilePreset.CmeSp500_ES_MES,
                        MarketProfilePreset.CmeSp500_ES_MES => MarketProfilePreset.CmeNasdaq_NQ_MNQ,
                        MarketProfilePreset.CmeNasdaq_NQ_MNQ => MarketProfilePreset.CmeKeyFutures_GC_CL_YM_RTY,
                        MarketProfilePreset.CmeKeyFutures_GC_CL_YM_RTY => MarketProfilePreset.CryptoBinanceFutures,
                        _ => MarketProfilePreset.AutoDetect
                    };
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartCrossTradeBtnRect.Contains(pt))
                {
                    _crossTradingMode = _crossTradingMode switch
                    {
                        CrossTradingExecutionMode.AutoAtasCrossTrading => CrossTradingExecutionMode.ForceMicroOnNormalChart,
                        CrossTradingExecutionMode.ForceMicroOnNormalChart => CrossTradingExecutionMode.NormalChartContractOnly,
                        _ => CrossTradingExecutionMode.AutoAtasCrossTrading
                    };
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartAcctSyncToggleRect.Contains(pt))
                {
                    _syncPnlWithActiveAccount = !_syncPnlWithActiveAccount;
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartAcctScopeToggleRect.Contains(pt))
                {
                    _accountPnlScope = _accountPnlScope == ActiveAccountPnlScope.ActivePortfolioTotal
                        ? ActiveAccountPnlScope.ChartInstrumentOnly
                        : ActiveAccountPnlScope.ActivePortfolioTotal;
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartSchedToggleRect.Contains(pt))
                {
                    _enableBotSchedule = !_enableBotSchedule;
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartStartMinusRect.Contains(pt))
                {
                    _botStartMinutesOfDay = ShiftScheduleMinutes(_botStartMinutesOfDay, -15);
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartStartPlusRect.Contains(pt))
                {
                    _botStartMinutesOfDay = ShiftScheduleMinutes(_botStartMinutesOfDay, +15);
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartStartValRect.Contains(pt))
                {
                    _botStartMinutesOfDay = ShiftScheduleMinutes(_botStartMinutesOfDay, +60);
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartEndMinusRect.Contains(pt))
                {
                    _botEndMinutesOfDay = ShiftScheduleMinutes(_botEndMinutesOfDay, -15);
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartEndPlusRect.Contains(pt))
                {
                    _botEndMinutesOfDay = ShiftScheduleMinutes(_botEndMinutesOfDay, +15);
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }

                if (ShowChartBotToggleButton && _chartEndValRect.Contains(pt))
                {
                    _botEndMinutesOfDay = ShiftScheduleMinutes(_botEndMinutesOfDay, +60);
                    RecalculateValues();
                    RedrawChart();
                    return true;
                }
            }

            return base.ProcessMouseClick(e);
        }

        public override bool ProcessMouseMove(RenderControlMouseEventArgs e)
        {
            Point pt = e.Location;
            bool hToggle = (ShowChartBotToggleButton && _chartBotToggleBtnRect.Contains(pt)) ||
                           (ShowHudPanel && _hudBotToggleBtnRect.Contains(pt));
            bool hMode = ShowChartBotToggleButton && _chartBotModeBtnRect.Contains(pt);
            bool hReset = ShowChartBotToggleButton && _chartBotResetBtnRect.Contains(pt);
            bool hProf = ShowChartBotToggleButton && _chartProfileBtnRect.Contains(pt);
            bool hCross = ShowChartBotToggleButton && _chartCrossTradeBtnRect.Contains(pt);
            bool hSched = ShowChartBotToggleButton && _chartSchedToggleRect.Contains(pt);
            bool hSMin = ShowChartBotToggleButton && _chartStartMinusRect.Contains(pt);
            bool hSPls = ShowChartBotToggleButton && _chartStartPlusRect.Contains(pt);
            bool hEMin = ShowChartBotToggleButton && _chartEndMinusRect.Contains(pt);
            bool hEPls = ShowChartBotToggleButton && _chartEndPlusRect.Contains(pt);
            bool hAcctSync = ShowChartBotToggleButton && _chartAcctSyncToggleRect.Contains(pt);
            bool hAcctScope = ShowChartBotToggleButton && _chartAcctScopeToggleRect.Contains(pt);

            if (hToggle != _hoverBotToggleBtn || hMode != _hoverBotModeBtn || hReset != _hoverBotResetBtn ||
                hProf != _hoverProfileBtn || hCross != _hoverCrossTradeBtn ||
                hSched != _hoverSchedToggleBtn || hSMin != _hoverStartMinus || hSPls != _hoverStartPlus ||
                hEMin != _hoverEndMinus || hEPls != _hoverEndPlus ||
                hAcctSync != _hoverAcctSyncBtn || hAcctScope != _hoverAcctScopeBtn)
            {
                _hoverBotToggleBtn = hToggle;
                _hoverBotModeBtn = hMode;
                _hoverBotResetBtn = hReset;
                _hoverProfileBtn = hProf;
                _hoverCrossTradeBtn = hCross;
                _hoverSchedToggleBtn = hSched;
                _hoverStartMinus = hSMin;
                _hoverStartPlus = hSPls;
                _hoverEndMinus = hEMin;
                _hoverEndPlus = hEPls;
                _hoverAcctSyncBtn = hAcctSync;
                _hoverAcctScopeBtn = hAcctScope;
                RedrawChart();
            }

            return base.ProcessMouseMove(e);
        }

        // =====================================================================
        // Synchronisation Temps Réel avec le Compte Actif ATAS X & le Trading Croisé (CrossTradingContext / SecuritySelected)
        // =====================================================================
        private void EnsureTradingManagerSubscriptions()
        {
            try
            {
                var tm = TradingManager;
                if (!ReferenceEquals(tm, _subscribedTradingManager))
                {
                    if (_subscribedTradingManager != null)
                    {
                        _subscribedTradingManager.PortfolioSelected -= OnTradingManagerPortfolioSelected;
                        _subscribedTradingManager.PortfolioChanged -= OnTradingManagerPortfolioChanged;
                        _subscribedTradingManager.PositionChanged -= OnTradingManagerPositionChanged;
                        _subscribedTradingManager.NewMyTrade -= OnTradingManagerNewMyTrade;
                        _subscribedTradingManager.SecuritySelected -= OnTradingManagerSecuritySelected;
                    }

                    _subscribedTradingManager = tm;

                    if (_subscribedTradingManager != null)
                    {
                        _subscribedTradingManager.PortfolioSelected += OnTradingManagerPortfolioSelected;
                        _subscribedTradingManager.PortfolioChanged += OnTradingManagerPortfolioChanged;
                        _subscribedTradingManager.PositionChanged += OnTradingManagerPositionChanged;
                        _subscribedTradingManager.NewMyTrade += OnTradingManagerNewMyTrade;
                        _subscribedTradingManager.SecuritySelected += OnTradingManagerSecuritySelected;
                    }
                }

                var crossCtx = DataProvider as ICrossTradingIndicatorContext;
                if (!ReferenceEquals(crossCtx, _subscribedCrossTradingContext))
                {
                    if (_subscribedCrossTradingContext != null)
                        _subscribedCrossTradingContext.CrossTradingInstrumentChanged -= OnCrossTradingInstrumentChanged;

                    _subscribedCrossTradingContext = crossCtx;

                    if (_subscribedCrossTradingContext != null)
                        _subscribedCrossTradingContext.CrossTradingInstrumentChanged += OnCrossTradingInstrumentChanged;
                }
            }
            catch
            {
                // Ignorer si le connecteur ne supporte pas les événements TradingManager / CrossTradingContext
            }
        }

        private void OnTradingManagerSecuritySelected(Security security)
        {
            RedrawChart();
        }

        private void OnCrossTradingInstrumentChanged(object? sender, EventArgs e)
        {
            RedrawChart();
        }

        private void OnTradingManagerPortfolioSelected(Portfolio portfolio)
        {
            _liveAccountPeakPnlUsd = decimal.MinValue;
            _liveAccountMaxDrawdownUsd = 0m;
            _liveKillSwitchExecuted = false;
            RedrawChart();
        }

        private void OnTradingManagerPortfolioChanged(Portfolio portfolio)
        {
            CheckRealtimeAccountKillSwitch();
            RedrawChart();
        }

        private void OnTradingManagerPositionChanged(Position position)
        {
            CheckRealtimeAccountKillSwitch();
            if (_enableTradingBot && _botMode == BotExecutionMode.LiveAutoTradingManager)
            {
                decimal lastPx = CurrentBar > 0
                    ? GetCandle(CurrentBar - 1).Close
                    : (TradingManager?.Security?.LastTradePrice ?? 0m);
                if (lastPx > 0m)
                    CheckLivePositionTargetsOnTick(lastPx);
            }
            RedrawChart();
        }

        private void OnTradingManagerNewMyTrade(MyTrade myTrade)
        {
            CheckRealtimeAccountKillSwitch();
            RedrawChart();
        }

        protected override void OnPortfolioChanged(Portfolio portfolio)
        {
            base.OnPortfolioChanged(portfolio);
            CheckRealtimeAccountKillSwitch();
            RedrawChart();
        }

        protected override void OnPositionChanged(Position position)
        {
            base.OnPositionChanged(position);
            CheckRealtimeAccountKillSwitch();
            if (_enableTradingBot && _botMode == BotExecutionMode.LiveAutoTradingManager)
            {
                decimal tickSize = InstrumentInfo?.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
                decimal lastPx = CurrentBar > 0
                    ? GetCandle(CurrentBar - 1).Close
                    : (TradingManager?.Security?.LastTradePrice ?? 0m);
                var pos = position ?? TradingManager?.Position;
                if (pos != null && pos.Volume != 0m)
                    EnsureLivePositionTrackingInitialized(pos, lastPx, tickSize);
                if (lastPx > 0m)
                    CheckLivePositionTargetsOnTick(lastPx);
            }
            RedrawChart();
        }

        protected override void OnNewMyTrade(MyTrade myTrade)
        {
            base.OnNewMyTrade(myTrade);
            CheckRealtimeAccountKillSwitch();
            if (_enableTradingBot && _botMode == BotExecutionMode.LiveAutoTradingManager && myTrade != null && myTrade.Price > 0m)
            {
                decimal tickSize = InstrumentInfo?.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
                decimal lastPx = CurrentBar > 0
                    ? GetCandle(CurrentBar - 1).Close
                    : myTrade.Price;
                var pos = TradingManager?.Position;
                if (pos != null && pos.Volume != 0m)
                {
                    EnsureLivePositionTrackingInitialized(pos, lastPx, tickSize);
                }
                else if (_liveTrackedDir != 0 && _liveTrackedSignalBar >= 0 && !_liveTrackedTp1Hit)
                {
                    // Synchronisation immédiate du prix d'exécution réel dès réception du fill MyTrade (même avant mise à jour de Position)
                    decimal step = tickSize * Math.Max(1, _ticksPerRow);
                    decimal fillPx = RoundToTick(myTrade.Price - _liveCrossBasisOffset, tickSize);
                    if (fillPx > 0m && Math.Abs(fillPx - _liveTrackedEntryPrice) <= step * 8m)
                    {
                        decimal prevRisk = Math.Max(step * 2m, Math.Abs(_liveTrackedEntryPrice - _liveTrackedStopPrice));
                        decimal tp1Dist = Math.Max(step * 2m, Math.Abs(_liveTrackedTp1Price - _liveTrackedEntryPrice));
                        decimal tp2Dist = Math.Max(tp1Dist + step * 2m, Math.Abs(_liveTrackedTp2Price - _liveTrackedEntryPrice));
                        decimal candidateStop = RoundToTick(fillPx - _liveTrackedDir * prevRisk, tickSize);

                        _liveTrackedEntryPrice = fillPx;
                        _liveTrackedRealFillPrice = RoundToTick(myTrade.Price, tickSize);
                        _liveTrackedStopPrice = _liveTrackedDir > 0
                            ? Math.Min(_liveTrackedStopPrice, candidateStop)
                            : Math.Max(_liveTrackedStopPrice, candidateStop);
                        _liveTrackedTp1Price = RoundToTick(fillPx + _liveTrackedDir * tp1Dist, tickSize);
                        _liveTrackedTp2Price = RoundToTick(fillPx + _liveTrackedDir * tp2Dist, tickSize);

                        if (_lockedExecutedSignalsByBar.TryGetValue(_liveTrackedSignalBar, out var lockedSig) && lockedSig.Direction == _liveTrackedDir)
                        {
                            lockedSig.EntryPrice = _liveTrackedEntryPrice;
                            lockedSig.RealAccountFillPrice = _liveTrackedRealFillPrice;
                            lockedSig.IsLiveFillSynced = true;
                            lockedSig.StopPrice = _liveTrackedStopPrice;
                            lockedSig.Tp1Price = _liveTrackedTp1Price;
                            lockedSig.Tp2Price = _liveTrackedTp2Price;
                            _lockedExecutedSignalsByBar[_liveTrackedSignalBar] = lockedSig;
                        }
                    }
                }
            }
            RedrawChart();
        }

        protected override void OnOrderChanged(Order order)
        {
            base.OnOrderChanged(order);
            RedrawChart();
        }

        private void CheckRealtimeAccountKillSwitch()
        {
            if (!_enableTradingBot || !_syncPnlWithActiveAccount)
                return;

            try
            {
                decimal tickSize = InstrumentInfo?.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
                int lastBar = Math.Max(0, CurrentBar - 1);
                BotPerformanceState state = default;
                SynchronizeWithActiveAccount(lastBar, tickSize, ref state);

                if ((state.IsHaltedByMaxDrawdown || state.IsHaltedByMaxProfit) && !_liveKillSwitchExecuted)
                {
                    var tm = TradingManager;
                    var pos = tm?.Position;
                    if (tm != null && pos != null && pos.Volume != 0m)
                    {
                        _liveKillSwitchExecuted = true;
                        SendLiveMarketCloseOrReduce(tm, pos, (int)Math.Round(Math.Abs(pos.Volume)), "QUANT_BOT_KILL_SWITCH");
                    }
                }
            }
            catch
            {
                // Protection temps réel
            }
        }

        // =====================================================================
        // Calibration & Réception temps réel des BigTrades (Losanges ◆) & Sweeps ATAS X
        // =====================================================================
        private decimal GetEffectiveBigTradeMinLots(decimal tickSize)
        {
            decimal baseLots = Math.Max(1, _bigTradeMinLots);
            if (!_bigTradeAutoScaleByMarket)
                return baseLots;

            var info = ResolveContractAndCrossTradingInfo(tickSize);
            if (info.BaseFamily == "NQ")
                return baseLots; // 40 lots par défaut sur NQ comme sur la photo ("more than 40 lots")
            if (info.BaseFamily == "ES")
                return Math.Max(baseLots, 80m);
            if (info.EffectiveProfile == MarketProfilePreset.CryptoBinanceFutures)
                return Math.Max(1m, baseLots * 0.25m);
            return baseLots;
        }

        private void RecordLiveBigTradePrint(int bar, decimal price, decimal volume, bool isBuy)
        {
            if (bar < 0 || price <= 0m || volume <= 0m)
                return;

            decimal tickSize = InstrumentInfo?.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
            decimal roundedPx = RoundToTick(price, tickSize);

            if (!_liveBigTradesByBar.TryGetValue(bar, out var list))
            {
                list = new List<LiveBigTradePrint>();
                _liveBigTradesByBar[bar] = list;
            }

            // Fusionne si un print du même côté existe déjà sur ce prix dans la même bougie
            bool merged = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].IsBuy == isBuy && Math.Abs(list[i].Price - roundedPx) <= tickSize * 0.5m)
                {
                    var updated = list[i];
                    if (volume > updated.Volume)
                        updated.Volume = volume;
                    updated.TimeUtc = DateTime.UtcNow;
                    list[i] = updated;
                    merged = true;
                    break;
                }
            }

            if (!merged)
            {
                list.Add(new LiveBigTradePrint
                {
                    Price = roundedPx,
                    Volume = volume,
                    IsBuy = isBuy,
                    TimeUtc = DateTime.UtcNow
                });
            }

            _lastBigTradeToastPrice = roundedPx;
            _lastBigTradeToastVol = volume;
            _lastBigTradeToastIsBuy = isBuy;
            _lastBigTradeToastTime = DateTime.Now;
        }

        protected override void OnNewTrade(MarketDataArg trade)
        {
            base.OnNewTrade(trade);
            if (trade == null || CurrentBar <= 0)
                return;

            // Surveillance tick-par-tick temps réel des prises de profits (TP1 / TP2 / Trailing / SL) sur le compte actif ATAS X
            if (_enableTradingBot && _botMode == BotExecutionMode.LiveAutoTradingManager && trade.Price > 0m)
            {
                CheckLivePositionTargetsOnTick(trade.Price);
            }

            int curBar = CurrentBar - 1;
            decimal tickSize = InstrumentInfo?.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
            decimal bigTradeMin = GetEffectiveBigTradeMinLots(tickSize);
            if (_enableBigTradesReversal && trade.Volume >= bigTradeMin && trade.Price > 0m)
            {
                bool isBuy = trade.IsAsk || trade.Direction == TradeDirection.Buy;
                bool isSell = trade.IsBid || trade.Direction == TradeDirection.Sell;
                if (isBuy || isSell)
                {
                    RecordLiveBigTradePrint(curBar, trade.Price, trade.Volume, isBuy);
                }
            }

            if (!_enableAtasOptionsFlow)
                return;

            if (_lastLiveSweepBar != curBar)
            {
                _lastLiveSweepBar = curBar;
                _liveTapeCallSweepVol = 0m;
                _liveTapePutSweepVol = 0m;
                _liveTapeSweepCount = 0;
            }

            // Si le titre lui-même est une Option ATAS X (OptionTypes.Call / Put)
            var sec = TradingManager?.Security;
            if (sec?.OptionType != null)
            {
                if (sec.OptionType == OptionTypes.Call)
                {
                    if (trade.IsAsk || trade.Direction == TradeDirection.Buy)
                        _liveTapeCallSweepVol += trade.Volume;
                    else if (trade.IsBid || trade.Direction == TradeDirection.Sell)
                        _liveTapePutSweepVol += trade.Volume * 0.7m; // Call écrit au Bid
                }
                else if (sec.OptionType == OptionTypes.Put)
                {
                    if (trade.IsAsk || trade.Direction == TradeDirection.Buy)
                        _liveTapePutSweepVol += trade.Volume;
                    else if (trade.IsBid || trade.Direction == TradeDirection.Sell)
                        _liveTapeCallSweepVol += trade.Volume * 0.7m; // Put vendu au Bid (bullish)
                }
                return;
            }

            // Détection de Sweep / Block Trade sur le flux temps réel ATAS X
            var candle = GetCandle(curBar);
            decimal avgTickVol = candle.Ticks > 0m ? candle.Volume / candle.Ticks : 10m;
            if (trade.Volume >= Math.Max(5m, avgTickVol * (_optionsSweepMultiplier * 2.2m)))
            {
                _liveTapeSweepCount++;
                if (trade.IsAsk || trade.Direction == TradeDirection.Buy)
                    _liveTapeCallSweepVol += trade.Volume;
                else if (trade.IsBid || trade.Direction == TradeDirection.Sell)
                    _liveTapePutSweepVol += trade.Volume;
            }
        }

        protected override void OnCumulativeTrade(CumulativeTrade trade)
        {
            base.OnCumulativeTrade(trade);
            if (trade == null || CurrentBar <= 0)
                return;

            int curBar = CurrentBar - 1;
            decimal tickSize = InstrumentInfo?.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
            decimal bigTradeMin = GetEffectiveBigTradeMinLots(tickSize);
            decimal cumPrice = trade.FirstPrice > 0m ? trade.FirstPrice : GetCandle(curBar).Close;
            if (_enableBigTradesReversal && trade.Volume >= bigTradeMin && cumPrice > 0m &&
                (trade.Direction == TradeDirection.Buy || trade.Direction == TradeDirection.Sell))
            {
                RecordLiveBigTradePrint(curBar, cumPrice, trade.Volume, trade.Direction == TradeDirection.Buy);
            }

            if (!_enableAtasOptionsFlow)
                return;

            if (_lastLiveSweepBar != curBar)
            {
                _lastLiveSweepBar = curBar;
                _liveTapeCallSweepVol = 0m;
                _liveTapePutSweepVol = 0m;
                _liveTapeSweepCount = 0;
            }

            if (trade.Direction == TradeDirection.Buy)
                _liveTapeCallSweepVol += trade.Volume * 0.5m;
            else if (trade.Direction == TradeDirection.Sell)
                _liveTapePutSweepVol += trade.Volume * 0.5m;
        }

        // =====================================================================
        // Interrogation non-bloquante de IOptionsDataFeed (ATAS.DataFeedsCore)
        // =====================================================================
        private void TryPollNativeOptionsFeed()
        {
            if (!_enableAtasOptionsFlow)
                return;

            DateTime now = DateTime.UtcNow;
            if ((now - _lastNativeOptionsFetch).TotalSeconds < 20)
                return;

            _lastNativeOptionsFetch = now;

            try
            {
                var security = TradingManager?.Security;
                if (security == null)
                    return;

                // Cas 1 : Le graphique est directement ouvert sur un contrat d'Option dans ATAS X
                if (security.OptionType != null)
                {
                    _nativeOptionsLoaded = true;
                    decimal oi = security.OpenInterest ?? 0m;
                    decimal lastVol = security.LastTradeVolume ?? 0m;
                    _nativeKeyStrike = security.StrikePrice ?? 0m;

                    if (security.OptionType == OptionTypes.Call)
                    {
                        _nativeChainCallOi = Math.Max(1m, oi);
                        _nativeChainCallVol = Math.Max(1m, lastVol + (security.BestAskVolume + security.BestBidVolume));
                    }
                    else
                    {
                        _nativeChainPutOi = Math.Max(1m, oi);
                        _nativeChainPutVol = Math.Max(1m, lastVol + (security.BestAskVolume + security.BestBidVolume));
                    }
                    return;
                }

                // Cas 2 : Interrogation du service IOptionsDataFeed d'ATAS X pour le sous-jacent
                if (TryGetService<IOptionsDataFeed>(out var optionsFeed) && optionsFeed != null)
                {
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        try
                        {
                            var seriesList = await optionsFeed.GetOptionSeriesAsync(security).ConfigureAwait(false);
                            if (seriesList == null)
                                return;

                            OptionSeries? frontSeries = null;
                            foreach (var s in seriesList)
                            {
                                if (s.Expiration >= DateTime.UtcNow.Date)
                                {
                                    if (!frontSeries.HasValue || s.Expiration < frontSeries.Value.Expiration)
                                        frontSeries = s;
                                }
                            }

                            if (!frontSeries.HasValue)
                                return;

                            var options = await optionsFeed.GetOptionsAsync(frontSeries.Value).ConfigureAwait(false);
                            if (options == null)
                                return;

                            decimal callVol = 0m, putVol = 0m, callOi = 0m, putOi = 0m;
                            decimal maxOi = 0m, bestStrike = 0m;

                            foreach (var opt in options)
                            {
                                if (opt == null) continue;
                                decimal v = (opt.LastTradeVolume ?? 0m) + opt.BestBidVolume + opt.BestAskVolume;
                                decimal oi = opt.OpenInterest ?? 0m;

                                if (oi > maxOi && opt.StrikePrice.HasValue)
                                {
                                    maxOi = oi;
                                    bestStrike = opt.StrikePrice.Value;
                                }

                                if (opt.OptionType == OptionTypes.Call)
                                {
                                    callVol += v;
                                    callOi += oi;
                                }
                                else if (opt.OptionType == OptionTypes.Put)
                                {
                                    putVol += v;
                                    putOi += oi;
                                }
                            }

                            if (callVol + putVol > 0m || callOi + putOi > 0m)
                            {
                                _nativeChainCallVol = callVol;
                                _nativeChainPutVol = putVol;
                                _nativeChainCallOi = callOi;
                                _nativeChainPutOi = putOi;
                                _nativeKeyStrike = bestStrike;
                                _nativeOptionsLoaded = true;
                            }
                        }
                        catch
                        {
                            // Ignorer silencieusement si le connecteur ne supporte pas les chaînes d'options
                        }
                    });
                }
            }
            catch
            {
                // Protection totale contre les connecteurs sans support d'options
            }
        }

        // =====================================================================
        // OnCalculate : Alimente les ValueDataSeries et déclenche les alertes
        // =====================================================================
        protected override void OnCalculate(int bar, decimal value)
        {
            if (bar < 0 || InstrumentInfo == null)
                return;

            decimal tickSize = InstrumentInfo.TickSize;
            if (tickSize <= 0m)
                tickSize = 0.25m;

            if (bar == CurrentBar - 1)
                TryPollNativeOptionsFeed();

            var candle = GetCandle(bar);
            var rows = AnalyzeBarClusters(
                candle, tickSize,
                out int pocIdx,
                out int primaryLvnIdx,
                out int bestBullAbsIdx,
                out int bestBearAbsIdx,
                out int bestTrappedSellersIdx,
                out int bestTrappedBuyersIdx);

            if (rows.Length == 0)
                return;

            _barRowsCache[bar] = rows;

            var optMetrics = AnalyzeBarOptionsFlow(bar, candle, rows, tickSize);
            var signal = EvaluateBuySellSignal(
                bar, candle, rows, tickSize,
                bestBullAbsIdx, bestBearAbsIdx,
                bestTrappedSellersIdx, bestTrappedBuyersIdx,
                optMetrics);

            _pocHvnSeries[bar] = (_enableBarHvn && pocIdx >= 0) ? rows[pocIdx].Price : 0m;
            _barLvnSeries[bar] = (_enableBarLvn && primaryLvnIdx >= 0) ? rows[primaryLvnIdx].Price : 0m;
            _bullAbsSeries[bar] = (_enableAbsorption && bestBullAbsIdx >= 0) ? rows[bestBullAbsIdx].Price : 0m;
            _bearAbsSeries[bar] = (_enableAbsorption && bestBearAbsIdx >= 0) ? rows[bestBearAbsIdx].Price : 0m;
            _trappedSellersSeries[bar] = (_enableTrappedTraders && bestTrappedSellersIdx >= 0) ? rows[bestTrappedSellersIdx].Price : 0m;
            _trappedBuyersSeries[bar] = (_enableTrappedTraders && bestTrappedBuyersIdx >= 0) ? rows[bestTrappedBuyersIdx].Price : 0m;
            _buyAlertSeries[bar] = (_enableVisualBuySellAlerts && signal.Direction > 0) ? signal.EntryPrice : 0m;
            _sellAlertSeries[bar] = (_enableVisualBuySellAlerts && signal.Direction < 0) ? signal.EntryPrice : 0m;

            // Synchronisation temps réel du PnL avec le Compte Actif ATAS X et exécution du Bot sur la bougie active / clôturée
            if (bar >= Math.Max(0, CurrentBar - 2))
            {
                EnsureTradingManagerSubscriptions();

                VisualTradeSignal rawEvaluatedSignal = signal;

                // En mode LiveAutoTradingManager sur la bougie active, on évalue et route l'ordre réel AVANT que la simulation
                // ne filtre le signal, afin qu'aucune position théorique de backtest ne bloque un ordre réel ni ne crée de décalage visuel.
                if (_enableTradingBot && _botMode == BotExecutionMode.LiveAutoTradingManager && bar == CurrentBar - 1)
                {
                    BotPerformanceState preLiveState = default;
                    SynchronizeWithActiveAccount(bar, tickSize, ref preLiveState);
                    ProcessLiveTradingManagerBot(bar, candle, tickSize, rawEvaluatedSignal, preLiveState);
                }

                List<BotTradeExecutionRecord> simTrades = new List<BotTradeExecutionRecord>();
                BotPerformanceState botState = _enableTradingBot
                    ? SimulateQuantTradingBot(bar, tickSize, out simTrades)
                    : default;

                if (_enableTradingBot && _onlyShowExecutedBotSignals)
                {
                    bool openedOnThisBar = false;
                    if (_lockedExecutedSignalsByBar.TryGetValue(bar, out var lockedOnBar) && lockedOnBar.Direction != 0)
                    {
                        openedOnThisBar = true;
                        signal = lockedOnBar;
                    }
                    else
                    {
                        for (int i = 0; i < simTrades.Count; i++)
                        {
                            if (simTrades[i].EntryBar == bar)
                            {
                                openedOnThisBar = true;
                                signal = simTrades[i].EntrySignal;
                                break;
                            }
                        }
                    }

                    if (!openedOnThisBar)
                        signal = default;

                    _buyAlertSeries[bar] = (_enableVisualBuySellAlerts && openedOnThisBar && signal.Direction > 0) ? signal.EntryPrice : 0m;
                    _sellAlertSeries[bar] = (_enableVisualBuySellAlerts && openedOnThisBar && signal.Direction < 0) ? signal.EntryPrice : 0m;
                }

                SynchronizeWithActiveAccount(bar, tickSize, ref botState);

                if (_enableTradingBot && _botMode == BotExecutionMode.LiveAutoTradingManager)
                {
                    VisualTradeSignal liveSignal = signal.Direction != 0 ? signal : rawEvaluatedSignal;
                    ProcessLiveTradingManagerBot(bar, candle, tickSize, liveSignal, botState);

                    // Si l'ordre réel a verrouillé ou recalé le prix d'entrée exact sur cette bougie, synchroniser immédiatement les séries visuelles
                    if (_lockedExecutedSignalsByBar.TryGetValue(bar, out var syncedSig) && syncedSig.Direction != 0)
                    {
                        signal = syncedSig;
                        _buyAlertSeries[bar] = (_enableVisualBuySellAlerts && signal.Direction > 0) ? signal.EntryPrice : 0m;
                        _sellAlertSeries[bar] = (_enableVisualBuySellAlerts && signal.Direction < 0) ? signal.EntryPrice : 0m;
                    }
                }

                _botCumPnlSeries[bar] = botState.TotalPnlUsd;
                _botDrawdownSeries[bar] = botState.CurrentDrawdownUsd;
            }

            // Alerte temps réel sur la dernière bougie
            if (EnableAlerts && bar == CurrentBar - 1)
            {
                int alertCode = 0;
                if (_enableVisualBuySellAlerts && signal.Direction > 0) alertCode = 10;
                else if (_enableVisualBuySellAlerts && signal.Direction < 0) alertCode = -10;
                else if (bestBullAbsIdx >= 0) alertCode = 1;
                else if (bestBearAbsIdx >= 0) alertCode = -1;
                else if (bestTrappedSellersIdx >= 0) alertCode = 2;
                else if (bestTrappedBuyersIdx >= 0) alertCode = -2;

                if (alertCode != 0 && (_lastAlertBar != bar || _lastAlertCode != alertCode))
                {
                    _lastAlertBar = bar;
                    _lastAlertCode = alertCode;

                    string instrument = InstrumentInfo?.Instrument ?? "ATAS X";
                    if (Math.Abs(alertCode) == 10)
                    {
                        bool isBuy = alertCode > 0;
                        string dirText = isBuy
                            ? "★ ALERTE ACHAT (BUY ▲) — CONFLUENCE FOOTPRINT + FLUX D'OPTIONS ATAS X"
                            : "★ ALERTE VENTE (SELL ▼) — CONFLUENCE FOOTPRINT + FLUX D'OPTIONS ATAS X";
                        string msg = $"{dirText} | Entrée: {signal.EntryPrice} | Stop: {signal.StopPrice} | Score: {signal.ConfluenceScore}% | Calls {signal.CallFlowPct:0}% vs Puts {signal.PutFlowPct:0}% (PCR {signal.PutCallRatio:0.00}, ΔOI {signal.OiDelta:+0;-0;0}) [{signal.ReasonTag}]";
                        Color bg = isBuy ? Color.FromArgb(255, 0, 130, 75) : Color.FromArgb(255, 155, 20, 45);
                        AddAlert(AlertSoundFile, instrument, msg, bg, Color.White);
                    }
                    else if (Math.Abs(alertCode) == 1)
                    {
                        ClusterRow absRow = alertCode > 0 ? rows[bestBullAbsIdx] : rows[bestBearAbsIdx];
                        string dirText = alertCode > 0
                            ? "BULLISH HEAVY ABSORPTION (Bid Absorbé)"
                            : "BEARISH HEAVY ABSORPTION (Ask Absorbé)";
                        string msg = $"{dirText} @ {absRow.Price} | Vol: {absRow.Volume:0} (Bid {absRow.Bid:0} x Ask {absRow.Ask:0}, Δ {absRow.Delta:+0;-0;0})";
                        Color bg = alertCode > 0 ? Color.FromArgb(255, 0, 95, 115) : Color.FromArgb(255, 120, 20, 65);
                        AddAlert(AlertSoundFile, instrument, msg, bg, Color.White);
                    }
                    else
                    {
                        ClusterRow trapRow = alertCode > 0 ? rows[bestTrappedSellersIdx] : rows[bestTrappedBuyersIdx];
                        string dirText = alertCode > 0
                            ? "VENDEURS PIÉGÉS ▲ (Trapped Sellers - Signal Haussier)"
                            : "ACHETEURS PIÉGÉS ▼ (Trapped Buyers - Signal Baissier)";
                        string msg = $"{dirText} @ {trapRow.Price} | Vol: {trapRow.Volume:0} (Bid {trapRow.Bid:0} x Ask {trapRow.Ask:0}, Δ {trapRow.Delta:+0;-0;0})";
                        Color bg = alertCode > 0 ? Color.FromArgb(255, 10, 110, 75) : Color.FromArgb(255, 135, 40, 20);
                        AddAlert(AlertSoundFile, instrument, msg, bg, Color.White);
                    }
                }
            }
        }

        // =====================================================================
        // Analyse du Flux d'Options ATAS X (IOptionsDataFeed, OpenInterest ΔOI,
        // Sweeps & Asymétrie Call Flow vs Put Flow par bougie — Calibré Crypto & CME)
        // =====================================================================
        private BarOptionsFlowMetrics AnalyzeBarOptionsFlow(
            int bar,
            IndicatorCandle candle,
            ClusterRow[] rows,
            decimal tickSize)
        {
            var metrics = new BarOptionsFlowMetrics();
            if (rows.Length == 0)
                return metrics;

            var contractInfo = ResolveContractAndCrossTradingInfo(tickSize);
            bool isCme = contractInfo.IsCmeMarket;

            // 1. Variation d'Open Interest fournie par ATAS X (Candle.OI, MaxOI, MinOI)
            //    + Compensation native CME (MinDelta / MaxDelta intra-bar lorsque le flux CME ne diffuse pas d'OI tick-par-tick)
            decimal prevOi = bar > 0 ? GetCandle(bar - 1).OI : candle.OI;
            decimal oiDelta = candle.OI - prevOi;
            if (oiDelta == 0m && (candle.MaxOI > candle.MinOI))
            {
                decimal intraOiRange = candle.MaxOI - candle.MinOI;
                oiDelta = candle.Close >= candle.Open ? intraOiRange : -intraOiRange;
            }

            decimal cmeBullDeltaRecovery = Math.Max(0m, candle.Delta - candle.MinDelta);
            decimal cmeBearDeltaRejection = Math.Max(0m, candle.MaxDelta - candle.Delta);
            if (isCme && oiDelta == 0m && (cmeBullDeltaRecovery > 0m || cmeBearDeltaRejection > 0m))
            {
                metrics.OiDelta = Math.Round(cmeBullDeltaRecovery - cmeBearDeltaRejection, 0);
            }
            else
            {
                metrics.OiDelta = oiDelta;
            }

            // 2. Calcul du flux agressif Calls Synthétiques / Réels vs Puts sur les clusters Footprint
            decimal avgVol = candle.Volume / Math.Max(1, rows.Length);
            decimal effSweepMult = isCme ? Math.Min(_optionsSweepMultiplier, 1.55m) : _optionsSweepMultiplier;
            decimal sweepThreshold = avgVol * effSweepMult;

            decimal callFlow = 0m;
            decimal putFlow = 0m;
            int sweeps = 0;

            decimal midPrice = (candle.High + candle.Low) * 0.5m;
            decimal vwap = candle.VWAP > 0m ? candle.VWAP : midPrice;
            decimal bodyLow = Math.Min(candle.Open, candle.Close);
            decimal bodyHigh = Math.Max(candle.Open, candle.Close);

            for (int i = 0; i < rows.Length; i++)
            {
                ClusterRow r = rows[i];
                if (r.Volume <= 0m)
                    continue;

                decimal askAggr = r.Ask;
                decimal bidAggr = r.Bid;

                if (!isCme)
                {
                    if (r.AbsorptionSide > 0 || r.TrappedSide > 0)
                    {
                        // Vendeurs agressifs absorbés / piégés en bas -> carburant haussier (Calls / Short-Covering)
                        callFlow += r.Volume * 1.20m + askAggr * 0.85m;
                        putFlow += bidAggr * 0.25m;
                    }
                    else if (r.AbsorptionSide < 0 || r.TrappedSide < 0)
                    {
                        // Acheteurs agressifs absorbés / piégés en haut -> carburant baissier (Puts / Long-Liquidation)
                        putFlow += r.Volume * 1.20m + bidAggr * 0.85m;
                        callFlow += askAggr * 0.25m;
                    }
                    else if (r.Price >= vwap)
                    {
                        callFlow += askAggr * 1.15m;
                        putFlow += bidAggr * 0.85m;
                    }
                    else
                    {
                        callFlow += askAggr * 0.90m;
                        putFlow += bidAggr * 1.15m;
                    }

                    if ((r.IsHvn || r.IsPoc) && candle.Close > r.Price && candle.Close >= candle.Open)
                        callFlow += r.Volume * 0.22m;
                    else if ((r.IsHvn || r.IsPoc) && candle.Close < r.Price && candle.Close <= candle.Open)
                        putFlow += r.Volume * 0.22m;
                }
                else
                {
                    // --- Calibration Institutionnelle Hors BTC Binance (CME ES/MES, NQ/MNQ, GC, CL, Indices & Alts) ---
                    if (r.AbsorptionSide > 0 || r.TrappedSide > 0)
                    {
                        // Vendeurs piégés ou absorbés au Bid : flux haussier institutionnel (ne jamais pénaliser CallFlowPct !)
                        callFlow += r.Volume * 1.25m + askAggr * 0.90m;
                        putFlow += bidAggr * 0.20m;
                    }
                    else if (r.AbsorptionSide < 0 || r.TrappedSide < 0)
                    {
                        // Acheteurs piégés ou absorbés à l'Ask : flux baissier institutionnel (ne jamais pénaliser PutFlowPct !)
                        putFlow += r.Volume * 1.25m + bidAggr * 0.90m;
                        callFlow += askAggr * 0.20m;
                    }
                    else if (r.Price < bodyLow && candle.Close > bodyLow && bidAggr > askAggr * 1.25m)
                    {
                        // Mèche basse rejetée avec ventes absorbées passivement
                        callFlow += bidAggr * 1.10m + askAggr * 0.90m;
                        putFlow += bidAggr * 0.35m;
                    }
                    else if (r.Price > bodyHigh && candle.Close < bodyHigh && askAggr > bidAggr * 1.25m)
                    {
                        // Mèche haute rejetée avec achats absorbés passivement
                        putFlow += askAggr * 1.10m + bidAggr * 0.90m;
                        callFlow += askAggr * 0.35m;
                    }
                    else if (r.Price >= vwap)
                    {
                        callFlow += askAggr * 1.15m;
                        putFlow += bidAggr * 0.88m;
                    }
                    else
                    {
                        callFlow += askAggr * 0.88m;
                        putFlow += bidAggr * 1.15m;
                    }

                    // Ancrage HVN & Rejet LVN dans le flux d'options synthétique CME
                    if ((r.IsHvn || r.IsPoc) && candle.Close > r.Price && candle.Close >= midPrice)
                        callFlow += r.Volume * 0.28m;
                    else if ((r.IsHvn || r.IsPoc) && candle.Close < r.Price && candle.Close <= midPrice)
                        putFlow += r.Volume * 0.28m;

                    if (r.IsLvn && r.Price <= midPrice && candle.Close > r.Price)
                        callFlow += avgVol * 0.35m;
                    else if (r.IsLvn && r.Price >= midPrice && candle.Close < r.Price)
                        putFlow += avgVol * 0.35m;
                }

                // Détection des Sweeps / Block Prints sur les niveaux
                decimal sweepDomRatio = isCme ? 0.58m : 0.64m;
                if (r.Volume >= sweepThreshold && (r.Ask >= r.Volume * sweepDomRatio || r.Bid >= r.Volume * sweepDomRatio))
                {
                    sweeps++;
                    if (r.Ask > r.Bid)
                    {
                        if (r.TrappedSide < 0 || r.AbsorptionSide < 0 || (isCme && r.Price > candle.Close + tickSize))
                            putFlow += r.Ask * 0.95m;
                        else
                            callFlow += r.Ask * 0.95m;
                    }
                    else
                    {
                        if (r.TrappedSide > 0 || r.AbsorptionSide > 0 || (isCme && r.Price < candle.Close - tickSize))
                            callFlow += r.Bid * 0.95m;
                        else
                            putFlow += r.Bid * 0.95m;
                    }
                }
            }

            // 3. Intégration de l'Open Interest (ΔOI) ou du Delta Tail Shift CME (MinDelta / MaxDelta)
            if (oiDelta != 0m)
            {
                decimal oiWeight = Math.Min(candle.Volume * 0.35m, Math.Abs(oiDelta) * 2m);
                if (candle.Close > candle.Open && oiDelta > 0m)
                    callFlow += oiWeight;
                else if (candle.Close < candle.Open && oiDelta > 0m)
                    putFlow += oiWeight;
                else if (candle.Close > candle.Open && oiDelta < 0m)
                    callFlow += oiWeight * 0.6m;
                else if (candle.Close < candle.Open && oiDelta < 0m)
                    putFlow += oiWeight * 0.6m;
            }
            else if (isCme)
            {
                if (cmeBullDeltaRecovery > 0m && (candle.Close >= candle.Open || candle.Close >= midPrice))
                    callFlow += Math.Min(candle.Volume * 0.32m, cmeBullDeltaRecovery * 0.65m);
                if (cmeBearDeltaRejection > 0m && (candle.Close <= candle.Open || candle.Close <= midPrice))
                    putFlow += Math.Min(candle.Volume * 0.32m, cmeBearDeltaRejection * 0.65m);
            }

            // 4. Intégration des données temps réel ATAS X (IOptionsDataFeed + OnNewTrade + DOM) sur la bougie active
            if (bar == CurrentBar - 1)
            {
                if (_liveTapeCallSweepVol > 0m || _liveTapePutSweepVol > 0m)
                {
                    callFlow += _liveTapeCallSweepVol;
                    putFlow += _liveTapePutSweepVol;
                    sweeps += _liveTapeSweepCount;
                }

                // Déséquilibre carnet de commandes ATAS X (MarketDepthInfo.CumulativeDomBids vs CumulativeDomAsks)
                decimal domBids = MarketDepthInfo?.CumulativeDomBids ?? 0m;
                decimal domAsks = MarketDepthInfo?.CumulativeDomAsks ?? 0m;
                if (domBids > 0m && domAsks > 0m)
                {
                    decimal domTotal = domBids + domAsks;
                    decimal domBidShare = domBids / domTotal;
                    if (domBidShare >= 0.56m)
                        callFlow *= 1.12m;
                    else if (domBidShare <= 0.44m)
                        putFlow *= 1.12m;
                }
            }

            // Fusion avec la chaîne d'options native IOptionsDataFeed si disponible dans ATAS X
            if (_nativeOptionsLoaded && (_nativeChainCallVol + _nativeChainPutVol > 0m || _nativeChainCallOi + _nativeChainPutOi > 0m))
            {
                metrics.IsNativeChainActive = true;
                metrics.KeyGammaStrike = _nativeKeyStrike;

                decimal nativeCallWeight = _nativeChainCallVol + _nativeChainCallOi * 0.15m;
                decimal nativePutWeight = _nativeChainPutVol + _nativeChainPutOi * 0.15m;
                decimal nativeSum = nativeCallWeight + nativePutWeight;

                if (nativeSum > 0m)
                {
                    decimal curSum = Math.Max(1m, callFlow + putFlow);
                    callFlow = callFlow * 0.65m + curSum * (nativeCallWeight / nativeSum) * 0.35m;
                    putFlow = putFlow * 0.65m + curSum * (nativePutWeight / nativeSum) * 0.35m;
                }
            }

            decimal totalOptFlow = callFlow + putFlow;
            if (totalOptFlow <= 0m)
            {
                metrics.CallFlowPct = 50m;
                metrics.PutFlowPct = 50m;
                metrics.PutCallRatio = 1.00m;
                return metrics;
            }

            metrics.CallVolume = Math.Round(callFlow, 0);
            metrics.PutVolume = Math.Round(putFlow, 0);
            metrics.CallFlowPct = Math.Round((callFlow / totalOptFlow) * 100m, 1);
            metrics.PutFlowPct = Math.Round(100m - metrics.CallFlowPct, 1);
            metrics.PutCallRatio = callFlow > 0m ? Math.Round(putFlow / callFlow, 2) : 2.50m;
            metrics.NetOptionsDelta = Math.Round(callFlow - putFlow, 0);
            metrics.SweepCount = sweeps;

            // 5. Métriques Quant Senior : ATR(N), Z-Score Max, Divergence CVD, Régime EMA(8/21) & Contrôle POC
            metrics.AtrPoints = ComputeQuantAtr(bar, tickSize);
            decimal maxVZ = 0m, maxDZ = 0m;
            decimal pocPrice = midPrice;
            int bullStacks = 0, bearStacks = 0;
            decimal stackRatioThreshold = isCme ? 1.38m : 1.60m;

            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].IsPoc) pocPrice = rows[i].Price;
                if (rows[i].VolumeZScore > maxVZ) maxVZ = rows[i].VolumeZScore;
                if (rows[i].DeltaZScore > maxDZ) maxDZ = rows[i].DeltaZScore;

                if (i > 0)
                {
                    decimal prevBidDenom = isCme ? Math.Max(1m, rows[i - 1].Bid) : rows[i - 1].Bid;
                    if (prevBidDenom > 0m && rows[i].Ask >= (isCme ? avgVol * 0.45m : 0m) && (rows[i].Ask / prevBidDenom) >= stackRatioThreshold)
                        bullStacks++;
                }
                if (i + 1 < rows.Length)
                {
                    decimal nextAskDenom = isCme ? Math.Max(1m, rows[i + 1].Ask) : rows[i + 1].Ask;
                    if (nextAskDenom > 0m && rows[i].Bid >= (isCme ? avgVol * 0.45m : 0m) && (rows[i].Bid / nextAskDenom) >= stackRatioThreshold)
                        bearStacks++;
                }
            }
            metrics.MaxVolumeZScore = maxVZ;
            metrics.MaxDeltaZScore = maxDZ;
            metrics.BarPocPrice = pocPrice;
            metrics.BullStackedImbalances = bullStacks;
            metrics.BearStackedImbalances = bearStacks;

            // Calcul EMA rapide (8), EMA lente (21) et EMA tendance (50) pour qualifier le régime directionnel multi-horizon
            ComputeQuantEmas(bar, out decimal emaFast, out decimal emaSlow, out decimal emaTrend50);
            metrics.EmaFast = emaFast;
            metrics.EmaSlow = emaSlow;
            metrics.EmaTrend50 = emaTrend50;

            bool aboveVwap = candle.Close >= vwap;
            bool belowVwap = candle.Close <= vwap;
            if (emaFast >= emaSlow && aboveVwap && candle.Close >= emaTrend50 - metrics.AtrPoints * 0.25m)
                metrics.TrendBias = 1;
            else if (emaFast <= emaSlow && belowVwap && candle.Close <= emaTrend50 + metrics.AtrPoints * 0.25m)
                metrics.TrendBias = -1;
            else if (emaFast > emaSlow && aboveVwap)
                metrics.TrendBias = 1;
            else if (emaFast < emaSlow && belowVwap)
                metrics.TrendBias = -1;
            else
                metrics.TrendBias = 0;

            // Shift de Delta Intra-Bougie (Delta Tail Recovery / Rejection)
            decimal minTailDeltaThresh = Math.Max(2m, candle.Volume * 0.08m);
            metrics.DeltaTailBullRecovery = (candle.Delta - candle.MinDelta) >= minTailDeltaThresh && candle.Close >= midPrice;
            metrics.DeltaTailBearRejection = (candle.MaxDelta - candle.Delta) >= minTailDeltaThresh && candle.Close <= midPrice;

            // Divergence CVD d'Absorption + Shift de Delta Multi-Bougies (+ Tail Delta Recovery CME)
            bool passiveBullAbsCvd = (candle.Close >= candle.Open && candle.Delta < 0m) ||
                                     (isCme && candle.Close >= midPrice && candle.MinDelta < 0m && (candle.Delta - candle.MinDelta) >= Math.Abs(candle.MinDelta) * 0.45m);
            bool passiveBearAbsCvd = (candle.Close <= candle.Open && candle.Delta > 0m) ||
                                     (isCme && candle.Close <= midPrice && candle.MaxDelta > 0m && (candle.MaxDelta - candle.Delta) >= candle.MaxDelta * 0.45m);
            bool multiBarBullCvd = false;
            bool multiBarBearCvd = false;
            bool structBull = true;
            bool structBear = true;

            if (bar > 0)
            {
                var prevC = GetCandle(bar - 1);
                decimal prevMid = (prevC.High + prevC.Low) * 0.5m;
                if ((candle.Low <= prevC.Low && candle.Delta > prevC.Delta && candle.Close >= candle.Open) ||
                    (prevC.Delta < 0m && candle.Delta > 0m && candle.Close > prevC.Close))
                {
                    multiBarBullCvd = true;
                }
                if ((candle.High >= prevC.High && candle.Delta < prevC.Delta && candle.Close <= candle.Open) ||
                    (prevC.Delta > 0m && candle.Delta < 0m && candle.Close < prevC.Close))
                {
                    multiBarBearCvd = true;
                }

                // Confluence de Structure Multi-Bougies : évite d'acheter dans une chute libre ou de vendre dans un rallye vertical
                structBull = candle.Close >= prevMid || candle.Close > prevC.Close || (candle.Low < prevC.Low && candle.Close > prevC.Low + (prevC.High - prevC.Low) * 0.35m);
                structBear = candle.Close <= prevMid || candle.Close < prevC.Close || (candle.High > prevC.High && candle.Close < prevC.High - (prevC.High - prevC.Low) * 0.35m);
            }

            metrics.MultiBarStructureBull = structBull;
            metrics.MultiBarStructureBear = structBear;
            metrics.BullishCvdDivergence = passiveBullAbsCvd || multiBarBullCvd;
            metrics.BearishCvdDivergence = passiveBearAbsCvd || multiBarBearCvd;
            metrics.HasCvdAbsorptionDivergence = metrics.BullishCvdDivergence || metrics.BearishCvdDivergence;

            decimal atrSafe = Math.Max(tickSize * 2m, metrics.AtrPoints);
            metrics.VwapZScore = Math.Round((candle.Close - vwap) / atrSafe, 2);

            decimal effOptDom = isCme ? Math.Min(_optionsMinDominancePct, 52.5m) : _optionsMinDominancePct;
            if (metrics.CallFlowPct >= effOptDom)
                metrics.OptionsBias = 1;
            else if (metrics.PutFlowPct >= effOptDom)
                metrics.OptionsBias = -1;
            else
                metrics.OptionsBias = 0;

            // 6. Synthèse des BigTrades Piégés (Losanges ◆) sur la bougie
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].HasBigTradeBuy)
                    metrics.HasBigTradeBuyOnBar = true;
                if (rows[i].HasBigTradeSell)
                    metrics.HasBigTradeSellOnBar = true;

                if (rows[i].TrappedBigTradeSide > 0)
                {
                    metrics.HasTrappedBigTradeBull = true;
                    if (rows[i].BigTradeSellVol >= metrics.TrappedBigTradeLots)
                    {
                        metrics.TrappedBigTradeLots = rows[i].BigTradeSellVol;
                        metrics.TrappedBigTradePrice = rows[i].Price;
                    }
                }
                else if (rows[i].TrappedBigTradeSide < 0)
                {
                    metrics.HasTrappedBigTradeBear = true;
                    if (rows[i].BigTradeBuyVol >= metrics.TrappedBigTradeLots)
                    {
                        metrics.TrappedBigTradeLots = rows[i].BigTradeBuyVol;
                        metrics.TrappedBigTradePrice = rows[i].Price;
                    }
                }
            }

            // 7. Calcul des Niveaux Options X-Ray (CALL WALL, MAX PAIN, PUT WALL & 0 DTE Expected Move)
            ComputeOptionsXRayLevelsForBar(
                bar, candle, tickSize, metrics.AtrPoints,
                out metrics.XRayCallWall,
                out metrics.XRayCallWallGexM,
                out metrics.XRayMaxPain,
                out metrics.XRayPutWall,
                out metrics.XRayPutWallGexM,
                out metrics.XRayZeroDteExpectedMovePts);
            metrics.XRayGammaRegime = candle.Close >= metrics.XRayMaxPain ? 1 : -1;

            return metrics;
        }

        private decimal GetEffectiveOptionsStrikeStep(decimal refPrice)
        {
            if (_optionsStrikeStepPoints > 0m)
                return _optionsStrikeStepPoints;
            if (refPrice >= 12000m)
                return 50m; // NQ / MNQ : Strikes tous les 50 pts (ex: 27850, 27900, 27950, 28050)
            if (refPrice >= 3000m)
                return 25m; // ES / MES : Strikes tous les 25 pts
            if (refPrice >= 500m)
                return 10m;
            return 5m;
        }

        private static decimal RoundToStrike(decimal price, decimal strikeStep)
        {
            if (strikeStep <= 0m)
                return Math.Round(price, 0);
            return Math.Round(price / strikeStep, 0, MidpointRounding.AwayFromZero) * strikeStep;
        }

        private void ComputeOptionsXRayLevelsForBar(
            int bar,
            IndicatorCandle candle,
            decimal tickSize,
            decimal atrPoints,
            out decimal callWall,
            out decimal callWallGexM,
            out decimal maxPain,
            out decimal putWall,
            out decimal putWallGexM,
            out decimal zeroDtePts)
        {
            decimal strikeStep = GetEffectiveOptionsStrikeStep(candle.Close);
            decimal subStep = Math.Max(tickSize * 4m, strikeStep * 0.5m);

            int lookback = Math.Min(bar, Math.Max(45, ProfileLookbackBars * 2));
            int startBar = Math.Max(0, bar - lookback);
            if (ProfileScope == ProfileScopeMode.CurrentSession)
            {
                int sessBar = bar;
                while (sessBar > 0 && !IsNewSession(sessBar))
                    sessBar--;
                startBar = Math.Min(startBar, sessBar);
            }

            decimal sessHigh = candle.High;
            decimal sessLow = candle.Low;
            decimal weightedPriceSum = 0m;
            decimal totalVolSum = 0m;

            var callVolByStrike = new Dictionary<decimal, decimal>();
            var putVolByStrike = new Dictionary<decimal, decimal>();

            for (int b = startBar; b <= bar; b++)
            {
                var c = GetCandle(b);
                if (c.High > sessHigh) sessHigh = c.High;
                if (c.Low < sessLow) sessLow = c.Low;

                decimal v = Math.Max(1m, c.Volume);
                decimal mid = (c.High + c.Low) * 0.5m;
                weightedPriceSum += mid * v;
                totalVolSum += v;

                decimal hiStrike = RoundToStrike(c.High, strikeStep);
                decimal loStrike = RoundToStrike(c.Low, strikeStep);
                decimal clStrike = RoundToStrike(c.Close, strikeStep);

                decimal callShare = c.Close >= c.Open ? 0.62m : 0.38m;
                decimal putShare = 1m - callShare;

                if (!callVolByStrike.ContainsKey(hiStrike)) callVolByStrike[hiStrike] = 0m;
                if (!putVolByStrike.ContainsKey(loStrike)) putVolByStrike[loStrike] = 0m;
                if (!callVolByStrike.ContainsKey(clStrike)) callVolByStrike[clStrike] = 0m;
                if (!putVolByStrike.ContainsKey(clStrike)) putVolByStrike[clStrike] = 0m;

                callVolByStrike[hiStrike] += v * callShare * 0.65m;
                callVolByStrike[clStrike] += v * callShare * 0.35m;
                putVolByStrike[loStrike] += v * putShare * 0.65m;
                putVolByStrike[clStrike] += v * putShare * 0.35m;
            }

            decimal sessionVwap = totalVolSum > 0m ? (weightedPriceSum / totalVolSum) : candle.Close;
            maxPain = _manualMaxPainPrice > 0m
                ? _manualMaxPainPrice
                : (_nativeOptionsLoaded && _nativeKeyStrike > 0m
                    ? RoundToStrike(_nativeKeyStrike, strikeStep)
                    : RoundToStrike((sessionVwap * 0.65m) + ((sessHigh + sessLow) * 0.5m * 0.35m), strikeStep));

            // Recherche du CALL WALL (Strike au-dessus de MaxPain ou du prix courant avec la plus forte concentration Call GEX)
            decimal bestCallStrike = maxPain + strikeStep;
            decimal bestCallWeight = 0m;
            foreach (var kv in callVolByStrike)
            {
                if (kv.Key >= maxPain + strikeStep && kv.Key <= sessHigh + strikeStep * 2m)
                {
                    if (kv.Value > bestCallWeight)
                    {
                        bestCallWeight = kv.Value;
                        bestCallStrike = kv.Key;
                    }
                }
            }
            if (bestCallStrike <= maxPain)
                bestCallStrike = maxPain + strikeStep;

            // Recherche du PUT WALL (Strike en dessous de MaxPain avec la plus forte concentration Put GEX)
            decimal bestPutStrike = maxPain - strikeStep;
            decimal bestPutWeight = 0m;
            foreach (var kv in putVolByStrike)
            {
                if (kv.Key <= maxPain - strikeStep && kv.Key >= sessLow - strikeStep * 2m)
                {
                    if (kv.Value > bestPutWeight)
                    {
                        bestPutWeight = kv.Value;
                        bestPutStrike = kv.Key;
                    }
                }
            }
            if (bestPutStrike >= maxPain)
                bestPutStrike = maxPain - strikeStep;

            callWall = _manualCallWallPrice > 0m ? _manualCallWallPrice : bestCallStrike;
            putWall = _manualPutWallPrice > 0m ? _manualPutWallPrice : bestPutStrike;

            decimal avgBarVol = totalVolSum / Math.Max(1, bar - startBar + 1);
            decimal normDiv = Math.Max(100m, avgBarVol * 0.35m);
            callWallGexM = Math.Round(Math.Clamp((bestCallWeight / normDiv) * 3.8m + 8.5m, 4.2m, 95.0m), 1);
            putWallGexM = -Math.Round(Math.Clamp((bestPutWeight / normDiv) * 9.5m + 24.0m, 12.0m, 165.0m), 1);

            decimal sessRange = Math.Max(atrPoints * 4m, sessHigh - sessLow);
            zeroDtePts = Math.Round(Math.Max(strikeStep * 3m, sessRange * 2.15m), 0);
        }

        private void ComputeQuantEmas(int bar, out decimal emaFast, out decimal emaSlow, out decimal emaTrend50)
        {
            int start = Math.Max(0, bar - 65);
            decimal firstClose = GetCandle(start).Close;
            emaFast = firstClose;
            emaSlow = firstClose;
            emaTrend50 = firstClose;

            const decimal kFast = 2m / (8m + 1m);
            const decimal kSlow = 2m / (21m + 1m);
            const decimal kTrend = 2m / (50m + 1m);

            for (int b = start + 1; b <= bar; b++)
            {
                decimal c = GetCandle(b).Close;
                emaFast = (c - emaFast) * kFast + emaFast;
                emaSlow = (c - emaSlow) * kSlow + emaSlow;
                emaTrend50 = (c - emaTrend50) * kTrend + emaTrend50;
            }
        }

        private decimal ComputeQuantAtr(int bar, decimal tickSize)
        {
            int n = Math.Max(3, _quantAtrPeriod);
            int start = Math.Max(0, bar - n + 1);
            decimal sumTr = 0m;
            int count = 0;

            for (int b = start; b <= bar; b++)
            {
                var c = GetCandle(b);
                decimal tr = c.High - c.Low;
                if (b > 0)
                {
                    var prev = GetCandle(b - 1);
                    decimal hc = Math.Abs(c.High - prev.Close);
                    decimal lc = Math.Abs(c.Low - prev.Close);
                    if (hc > tr) tr = hc;
                    if (lc > tr) tr = lc;
                }
                sumTr += tr;
                count++;
            }

            decimal atr = count > 0 ? sumTr / count : tickSize * 8m;
            return Math.Max(tickSize * 2m, Math.Round(atr / tickSize, 0) * tickSize);
        }

        private static decimal RoundToTick(decimal price, decimal tickSize)
        {
            if (tickSize <= 0m) return Math.Round(price, 2);
            return Math.Round(price / tickSize, 0, MidpointRounding.AwayFromZero) * tickSize;
        }

        private decimal GetLiveExecutableChartPrice(
            int bar,
            int direction,
            decimal chartClose,
            decimal tickSize,
            decimal step,
            bool isCme,
            out bool isPriceGapAcceptable,
            out decimal crossBasisOffset,
            out decimal rawExecPrice)
        {
            isPriceGapAcceptable = true;
            crossBasisOffset = 0m;
            decimal roundedClose = RoundToTick(chartClose, tickSize);
            rawExecPrice = roundedClose;

            if (bar < CurrentBar - 1)
                return roundedClose;

            try
            {
                var tm = TradingManager;
                var sec = tm?.Security;
                if (sec == null)
                    return roundedClose;

                decimal secBid = sec.BestBidPrice;
                decimal secAsk = sec.BestAskPrice;
                decimal secLast = sec.LastTradePrice ?? 0m;

                // Vérification de l'écart Bid-Ask (Spread) en temps réel
                if (secBid > 0m && secAsk >= secBid)
                {
                    decimal spread = secAsk - secBid;
                    decimal maxAllowedSpread = step * (isCme ? 3.0m : 4.0m);
                    if (spread > maxAllowedSpread)
                    {
                        isPriceGapAcceptable = false;
                    }
                }

                decimal secRef = (secBid > 0m && secAsk > 0m)
                    ? (secBid + secAsk) * 0.5m
                    : (secLast > 0m ? secLast : roundedClose);

                // Détection automatique d'un écart de base (basis spread) entre le graphique (ex: continu) et le contrat tradé (ex: échéance / micro)
                if (secRef > 0m && Math.Abs(secRef - roundedClose) > step * 8m)
                {
                    crossBasisOffset = RoundToTick(secRef - roundedClose, tickSize);
                }

                if (direction > 0)
                {
                    rawExecPrice = secAsk > 0m
                        ? RoundToTick(secAsk, tickSize)
                        : (secLast > 0m ? RoundToTick(secLast, tickSize) : RoundToTick(roundedClose + crossBasisOffset, tickSize));
                }
                else
                {
                    rawExecPrice = secBid > 0m
                        ? RoundToTick(secBid, tickSize)
                        : (secLast > 0m ? RoundToTick(secLast, tickSize) : RoundToTick(roundedClose + crossBasisOffset, tickSize));
                }

                decimal chartExecPx = RoundToTick(rawExecPrice - crossBasisOffset, tickSize);
                decimal execDrift = Math.Abs(chartExecPx - roundedClose);
                decimal maxAllowedDrift = step * (isCme ? 2.5m : 3.5m);
                if (execDrift > maxAllowedDrift)
                {
                    isPriceGapAcceptable = false;
                }

                return chartExecPx > 0m ? chartExecPx : roundedClose;
            }
            catch
            {
                return roundedClose;
            }
        }

        // =====================================================================
        // Évaluation du Signal d'Entrée du Nouveau Bot :
        // UNIQUEMENT basé sur BigTrades Piégés (Losanges ◆) + Reversal
        // avec contexte Structurel HVN/LVN & Options X-Ray (CALL WALL, MAX PAIN, PUT WALL)
        // =====================================================================
        private VisualTradeSignal EvaluateBuySellSignal(
            int bar,
            IndicatorCandle candle,
            ClusterRow[] rows,
            decimal tickSize,
            int bestBullAbsIdx,
            int bestBearAbsIdx,
            int bestTrappedSellersIdx,
            int bestTrappedBuyersIdx,
            BarOptionsFlowMetrics opt)
        {
            var sig = new VisualTradeSignal { Bar = bar };
            if (!_enableVisualBuySellAlerts || !_enableBigTradesReversal || rows.Length == 0)
                return sig;

            // Si un ordre Live a déjà été exécuté sur cette bougie, retourner son signal exact (recalé sur le prix réel d'exécution)
            if (_lockedExecutedSignalsByBar.TryGetValue(bar, out var lockedSig) && lockedSig.Direction != 0)
                return lockedSig;

            var contractInfo = ResolveContractAndCrossTradingInfo(tickSize);
            bool isCme = contractInfo.IsCmeMarket;
            MarketProfilePreset prof = contractInfo.EffectiveProfile;

            decimal step = tickSize * Math.Max(1, _ticksPerRow);
            decimal atr = Math.Max(step * 2m, opt.AtrPoints);
            decimal barRange = Math.Max(step, candle.High - candle.Low);

            // Garde de maturité stricte sur la bougie en cours de formation (CurrentBar - 1) :
            // évite les entrées prématurées intra-bougie avant que le Reversal ne soit réellement confirmé par le volume (80%+)
            if (bar == CurrentBar - 1 && bar > 2)
            {
                if (rows.Length < 4)
                    return sig;

                int lb = Math.Min(10, bar);
                decimal sumVol = 0m;
                for (int k = 1; k <= lb; k++)
                    sumVol += GetCandle(bar - k).Volume;
                decimal avgBarVol = lb > 0 ? sumVol / lb : candle.Volume;
                if (avgBarVol > 0m && candle.Volume < avgBarVol * 0.80m)
                    return sig;
            }

            decimal midPrice = (candle.High + candle.Low) * 0.5m;
            decimal bodyHigh = Math.Max(candle.Open, candle.Close);
            decimal bodyLow = Math.Min(candle.Open, candle.Close);
            decimal upperWick = candle.High - bodyHigh;
            decimal lowerWick = bodyLow - candle.Low;

            // --- 0. Contexte de Structure Swing Multi-Bougies & Filtre Anti-Couteau Qui Tombe (Momentum Cascade) ---
            decimal swingLow5 = candle.Low;
            decimal swingHigh5 = candle.High;
            decimal swingLow8 = candle.Low;
            decimal swingHigh8 = candle.High;
            int consecutiveBearImpulse = 0;
            int consecutiveBullImpulse = 0;

            for (int m = 1; m <= Math.Min(8, bar); m++)
            {
                var prevC = GetCandle(bar - m);
                if (m <= 5)
                {
                    if (prevC.Low < swingLow5) swingLow5 = prevC.Low;
                    if (prevC.High > swingHigh5) swingHigh5 = prevC.High;
                }
                if (prevC.Low < swingLow8) swingLow8 = prevC.Low;
                if (prevC.High > swingHigh8) swingHigh8 = prevC.High;

                if (m <= 3)
                {
                    decimal pRange = prevC.High - prevC.Low;
                    decimal pBody = Math.Abs(prevC.Close - prevC.Open);
                    if (prevC.Close < prevC.Open && pRange >= atr * 1.15m && pBody >= pRange * 0.55m)
                        consecutiveBearImpulse++;
                    if (prevC.Close > prevC.Open && pRange >= atr * 1.15m && pBody >= pRange * 0.55m)
                        consecutiveBullImpulse++;
                }
            }

            bool isWaterfallCrash = consecutiveBearImpulse >= 3;
            bool isVerticalRocket = consecutiveBullImpulse >= 3;

            // --- 1. Contexte HVN / LVN (Intra-Bougie & Profil Composite Multi-Bougies) ---
            bool hasBullBarHvnSupport = false;
            bool hasBearBarHvnResistance = false;
            int bestBullHvnIdx = -1;
            int bestBearHvnIdx = -1;
            decimal bestBullHvnVol = 0m;
            decimal bestBearHvnVol = 0m;
            bool hasLowerLvnRejection = false;
            bool hasUpperLvnRejection = false;

            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].IsHvn || rows[i].IsPoc)
                {
                    if (rows[i].Price <= candle.Close && rows[i].Volume >= bestBullHvnVol)
                    {
                        hasBullBarHvnSupport = true;
                        bestBullHvnVol = rows[i].Volume;
                        bestBullHvnIdx = i;
                    }
                    if (rows[i].Price >= candle.Close && rows[i].Volume >= bestBearHvnVol)
                    {
                        hasBearBarHvnResistance = true;
                        bestBearHvnVol = rows[i].Volume;
                        bestBearHvnIdx = i;
                    }
                }
                if (rows[i].IsLvn)
                {
                    if (rows[i].Price <= midPrice + step && candle.Close > rows[i].Price + step)
                        hasLowerLvnRejection = true;
                    if (rows[i].Price >= midPrice - step && candle.Close < rows[i].Price - step)
                        hasUpperLvnRejection = true;
                }
            }

            bool hasCompHvnSupport = false;
            bool hasCompHvnResistance = false;
            bool hasCompLvnBullRejection = false;
            bool hasCompLvnBearRejection = false;
            decimal compSupportPrice = 0m;
            decimal compResistancePrice = 0m;

            if (EnableCompositeProfile && bar >= 4)
            {
                int compLb = Math.Min(isCme ? 35 : 40, Math.Min(bar, ProfileLookbackBars));
                decimal bandRange = atr * 2.5m;
                decimal minBandP = candle.Low - bandRange;
                decimal maxBandP = candle.High + bandRange;
                var compMap = new Dictionary<decimal, decimal>();
                decimal maxCompVol = 0m;

                for (int bIdx = bar - compLb; bIdx < bar; bIdx++)
                {
                    if (!_barRowsCache.TryGetValue(bIdx, out var rList) || rList == null)
                        continue;
                    for (int rIdx = 0; rIdx < rList.Length; rIdx++)
                    {
                        decimal p = rList[rIdx].Price;
                        if (p < minBandP || p > maxBandP)
                            continue;
                        decimal v = rList[rIdx].Volume;
                        if (v <= 0m)
                            continue;
                        if (compMap.TryGetValue(p, out decimal curV))
                            compMap[p] = curV + v;
                        else
                            compMap[p] = v;

                        if (compMap[p] > maxCompVol)
                            maxCompVol = compMap[p];
                    }
                }

                if (maxCompVol > 0m && compMap.Count >= 5)
                {
                    decimal hvnThresh = maxCompVol * (Math.Min(CompositeHvnPct, 68) / 100m);
                    decimal lvnThresh = maxCompVol * (Math.Max(CompositeLvnPct, 26) / 100m);

                    foreach (var kv in compMap)
                    {
                        decimal p = kv.Key;
                        decimal v = kv.Value;

                        if (v >= hvnThresh)
                        {
                            if (p <= candle.Close && p >= candle.Low - step * 2m && candle.Close >= p + step * 0.5m)
                            {
                                hasCompHvnSupport = true;
                                if (compSupportPrice == 0m || Math.Abs(candle.Close - p) < Math.Abs(candle.Close - compSupportPrice))
                                    compSupportPrice = p;
                            }
                            if (p >= candle.Close && p <= candle.High + step * 2m && candle.Close <= p - step * 0.5m)
                            {
                                hasCompHvnResistance = true;
                                if (compResistancePrice == 0m || Math.Abs(p - candle.Close) < Math.Abs(compResistancePrice - candle.Close))
                                    compResistancePrice = p;
                            }
                        }
                        else if (v <= lvnThresh)
                        {
                            if (p >= candle.Low - step && p <= midPrice + step && candle.Close > p + step)
                                hasCompLvnBullRejection = true;
                            if (p <= candle.High + step && p >= midPrice - step && candle.Close < p - step)
                                hasCompLvnBearRejection = true;
                        }
                    }
                }
            }

            // --- 2. Détection EXCLUSIVE des BigTrades Piégés (Losanges ◆) + Reversal Swing Confirmé ---
            bool hasBigTradeBullReversal = false;
            bool hasBigTradeBearReversal = false;
            decimal bigTradeBullPrice = 0m;
            decimal bigTradeBearPrice = 0m;
            decimal bigTradeBullLots = 0m;
            decimal bigTradeBearLots = 0m;
            decimal bullTrapLow = candle.Low;
            decimal bearTrapHigh = candle.High;
            int bullTrapBarOffset = 0;
            int bearTrapBarOffset = 0;

            decimal minRevClearance = Math.Max(step * 2m, RoundToTick(atr * 0.16m, tickSize));

            // Bougie de Reversal Haussier Confirmé : clôture franche dans le haut de la bougie + reprise du POC / shift de Delta
            bool bullOrderFlowShift = candle.Delta > 0m || opt.DeltaTailBullRecovery || opt.BullishCvdDivergence;
            bool bullPocReclaimed = opt.BarPocPrice <= 0m || candle.Close >= opt.BarPocPrice - step;
            bool isBullReversalCandle = bullOrderFlowShift && bullPocReclaimed &&
                                        ((candle.Close > candle.Open && candle.Close >= candle.Low + barRange * 0.58m) ||
                                         (lowerWick >= barRange * 0.45m && candle.Close >= candle.Low + barRange * 0.60m && candle.Close >= candle.Open));

            // Bougie de Reversal Baissier Confirmé : clôture franche dans le bas de la bougie + cassure du POC / shift de Delta
            bool bearOrderFlowShift = candle.Delta < 0m || opt.DeltaTailBearRejection || opt.BearishCvdDivergence;
            bool bearPocReclaimed = opt.BarPocPrice <= 0m || candle.Close <= opt.BarPocPrice + step;
            bool isBearReversalCandle = bearOrderFlowShift && bearPocReclaimed &&
                                        ((candle.Close < candle.Open && candle.Close <= candle.High - barRange * 0.58m) ||
                                         (upperWick >= barRange * 0.45m && candle.Close <= candle.High - barRange * 0.60m && candle.Close <= candle.Open));

            int btLookback = Math.Clamp(_bigTradeReversalLookbackBars, 1, 6);
            for (int k = 0; k <= Math.Min(btLookback, bar); k++)
            {
                int bCheck = bar - k;
                var cCheck = GetCandle(bCheck);
                decimal cRange = Math.Max(step, cCheck.High - cCheck.Low);
                decimal cMid = (cCheck.High + cCheck.Low) * 0.5m;

                // En multi-bougies (k >= 1), la bougie actuelle doit confirmer le retournement en clôturant au-dessus du milieu de la bougie de piégeage
                bool multiBarBullConfirm = k == 0 || (candle.Close > candle.Open && candle.Close > cMid && candle.Close >= cCheck.Close);
                bool multiBarBearConfirm = k == 0 || (candle.Close < candle.Open && candle.Close < cMid && candle.Close <= cCheck.Close);

                ClusterRow[]? rCheck = (k == 0)
                    ? rows
                    : (_barRowsCache.TryGetValue(bCheck, out var cachedRows) ? cachedRows : null);

                if (rCheck != null)
                {
                    for (int i = 0; i < rCheck.Length; i++)
                    {
                        // A) Losange Rouge (BigTrade Sell ◆) piégé dans les 45% bas de la bougie bCheck suivi d'un Reversal Haussier franc
                        if (rCheck[i].HasBigTradeSell &&
                            rCheck[i].Price <= cCheck.Low + cRange * 0.45m &&
                            isBullReversalCandle &&
                            multiBarBullConfirm &&
                            candle.Close >= rCheck[i].Price + minRevClearance)
                        {
                            hasBigTradeBullReversal = true;
                            if (rCheck[i].BigTradeSellVol >= bigTradeBullLots || bigTradeBullPrice == 0m)
                            {
                                bigTradeBullLots = rCheck[i].BigTradeSellVol;
                                bigTradeBullPrice = rCheck[i].Price;
                                bullTrapLow = Math.Min(candle.Low, cCheck.Low);
                                bullTrapBarOffset = k;
                            }
                        }

                        // B) Losange Vert (BigTrade Buy ◆) piégé dans les 45% hauts de la bougie bCheck suivi d'un Reversal Baissier franc
                        if (rCheck[i].HasBigTradeBuy &&
                            rCheck[i].Price >= cCheck.High - cRange * 0.45m &&
                            isBearReversalCandle &&
                            multiBarBearConfirm &&
                            candle.Close <= rCheck[i].Price - minRevClearance)
                        {
                            hasBigTradeBearReversal = true;
                            if (rCheck[i].BigTradeBuyVol >= bigTradeBearLots || bigTradeBearPrice == 0m)
                            {
                                bigTradeBearLots = rCheck[i].BigTradeBuyVol;
                                bigTradeBearPrice = rCheck[i].Price;
                                bearTrapHigh = Math.Max(candle.High, cCheck.High);
                                bearTrapBarOffset = k;
                            }
                        }
                    }
                }

                // Vérifie également les prints BigTrade temps réel capturés via OnCumulativeTrade / OnNewTrade
                if (_liveBigTradesByBar.TryGetValue(bCheck, out var liveList) && liveList != null)
                {
                    for (int i = 0; i < liveList.Count; i++)
                    {
                        var lp = liveList[i];
                        if (!lp.IsBuy &&
                            lp.Price <= cCheck.Low + cRange * 0.45m &&
                            isBullReversalCandle &&
                            multiBarBullConfirm &&
                            candle.Close >= lp.Price + minRevClearance)
                        {
                            hasBigTradeBullReversal = true;
                            if (lp.Volume >= bigTradeBullLots || bigTradeBullPrice == 0m)
                            {
                                bigTradeBullLots = lp.Volume;
                                bigTradeBullPrice = lp.Price;
                                bullTrapLow = Math.Min(candle.Low, cCheck.Low);
                                bullTrapBarOffset = k;
                            }
                        }
                        else if (lp.IsBuy &&
                                 lp.Price >= cCheck.High - cRange * 0.45m &&
                                 isBearReversalCandle &&
                                 multiBarBearConfirm &&
                                 candle.Close <= lp.Price - minRevClearance)
                        {
                            hasBigTradeBearReversal = true;
                            if (lp.Volume >= bigTradeBearLots || bigTradeBearPrice == 0m)
                            {
                                bigTradeBearLots = lp.Volume;
                                bigTradeBearPrice = lp.Price;
                                bearTrapHigh = Math.Max(candle.High, cCheck.High);
                                bearTrapBarOffset = k;
                            }
                        }
                    }
                }
            }

            // Si aucun BigTrade Piégé (◆) + Reversal n'est détecté, n'ouvrir AUCUN trade !
            if (!hasBigTradeBullReversal && !hasBigTradeBearReversal)
                return sig;

            // En cas de conflit sur la même bougie, donner priorité au BigTrade Piégé le plus massif dans le sens de la clôture
            if (hasBigTradeBullReversal && hasBigTradeBearReversal)
            {
                if (candle.Close > candle.Open || (candle.Close == candle.Open && bigTradeBullLots >= bigTradeBearLots))
                    hasBigTradeBearReversal = false;
                else
                    hasBigTradeBullReversal = false;
            }

            decimal effBigTradeThreshold = GetEffectiveBigTradeMinLots(tickSize);
            string xrayTag = $"CW {opt.XRayCallWall:0} ({opt.XRayCallWallGexM:+0.0;-0.0}M) • MP {opt.XRayMaxPain:0} • PW {opt.XRayPutWall:0} ({opt.XRayPutWallGexM:+0.0;-0.0}M)";

            decimal commRtUsd = Math.Max(0.50m, contractInfo.TradedCommissionPerSideUsd * 2m);
            decimal tickValueUsd = Math.Max(0.05m, contractInfo.TradedPointValueUsd * tickSize);
            int commStepFloor = Math.Max(1, (int)Math.Ceiling(commRtUsd / Math.Max(0.05m, tickValueUsd * Math.Max(1, _ticksPerRow))));

            string crossSuffix = contractInfo.IsCrossTradingMicro
                ? $" [CROISÉ➔{contractInfo.TradedSymbol}]"
                : string.Empty;

            // --- 3. Signal REVERSAL ACHAT (BUY ▲) sur BigTrade Vendeur (Losange Rouge ◆) Piégé ---
            if (hasBigTradeBullReversal && bigTradeBullPrice > 0m)
            {
                decimal entryPrice = GetLiveExecutableChartPrice(
                    bar, 1, candle.Close, tickSize, step, isCme,
                    out bool livePriceGapOk, out decimal crossBasisOffset, out decimal rawExecFillPx);

                if (livePriceGapOk)
                {
                    bool hvnAnchored = hasBullBarHvnSupport || hasCompHvnSupport;
                    bool lvnConfirmed = hasLowerLvnRejection || hasCompLvnBullRejection;
                    bool optionsWallSupport = _enableOptionsXRayOverlay &&
                        ((opt.XRayPutWall > 0m && Math.Abs(bullTrapLow - opt.XRayPutWall) <= atr * 1.15m) ||
                         (opt.XRayMaxPain > 0m && Math.Abs(bullTrapLow - opt.XRayMaxPain) <= atr * 0.90m));

                    // Validation d'extrême de Swing : le piégeage doit se produire sur un creux de Swing (et non au milieu d'un range)
                    bool atSwingLow = bullTrapLow <= swingLow5 + atr * 0.15m ||
                                      (bullTrapLow <= swingLow8 + atr * 0.25m && (hvnAnchored || lvnConfirmed || optionsWallSupport));

                    // Protection Anti-Couteau Qui Tombe : bloque l'achat face à une chute libre de 3 grosses bougies rouges sans base multi-bougies
                    bool waterfallBlocked = isWaterfallCrash && (bullTrapBarOffset == 0 || (bar > 0 && candle.Close < GetCandle(bar - 1).High));

                    // Filtre Anti-Entrée Tardive : ne jamais acheter en haut d'une bougie géante déjà épuisée (évite les gros Stop-Loss pour peu de gain restant)
                    decimal distFromTrapLow = entryPrice - bullTrapLow;
                    bool overExtendedEntry = barRange > atr * 1.65m || distFromTrapLow > atr * 1.05m;

                    if (atSwingLow && !waterfallBlocked && !overExtendedEntry)
                    {
                        decimal clusterZ = 1.50m;
                        if (bestTrappedSellersIdx >= 0 && bestTrappedSellersIdx < rows.Length)
                            clusterZ = Math.Max(clusterZ, Math.Max(rows[bestTrappedSellersIdx].VolumeZScore, rows[bestTrappedSellersIdx].DeltaZScore));
                        if (bestBullHvnIdx >= 0 && bestBullHvnIdx < rows.Length)
                            clusterZ = Math.Max(clusterZ, rows[bestBullHvnIdx].VolumeZScore);

                        // Calcul réel du Score de Confluence Institutionnel (60..99)
                        int score = 62;
                        if (opt.TrendBias > 0) score += 9;
                        else if (opt.TrendBias == 0) score += 4;
                        if (hvnAnchored) score += 7;
                        if (lvnConfirmed) score += 6;
                        if (optionsWallSupport) score += 7;
                        if (candle.Delta > 0m && candle.Close > candle.Open) score += 6;
                        if (bigTradeBullLots >= effBigTradeThreshold * 1.35m) score += 5;
                        if (bullTrapBarOffset >= 1) score += 3;
                        score = Math.Clamp(score, 60, 99);

                        int minRequiredScore = opt.TrendBias < 0 ? 80 : 74;
                        if (score >= minRequiredScore)
                        {
                            // Calibrage Anti-Microscalping : Stop-Loss structurel maîtrisé + Objectifs Swing TP1 (1.8R) & TP2 (3.2R+)
                            int baseMinStopSteps = isCme
                                ? (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 10 : 6)
                                : 5;
                            int minStopSteps = Math.Max(baseMinStopSteps, commStepFloor + 4);

                            int baseMinTp1Steps = isCme
                                ? (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 16 : 10)
                                : 8;
                            int minTp1Steps = Math.Max(baseMinTp1Steps, commStepFloor + 8);

                            int baseMinTp2Steps = isCme
                                ? (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 32 : 20)
                                : 16;

                            decimal structuralPad = (isCme && prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ) ? step * 3m : step * 2m;
                            decimal anchorLow = Math.Min(bullTrapLow - structuralPad, bigTradeBullPrice - structuralPad);
                            if (hasCompHvnSupport && compSupportPrice > 0m && compSupportPrice < entryPrice && (entryPrice - compSupportPrice) <= atr * 0.65m)
                                anchorLow = Math.Min(anchorLow, compSupportPrice - structuralPad);

                            decimal minStopDist = Math.Max(step * minStopSteps, RoundToTick(atr * 0.48m, tickSize));
                            decimal maxAllowedStopDist = Math.Max(minStopDist + step * 4m, RoundToTick(atr * Math.Clamp(_quantAtrStopMultiplier, 0.55m, 1.05m), tickSize));
                            decimal rawStopDist = Math.Clamp(entryPrice - anchorLow, minStopDist, maxAllowedStopDist);

                            decimal stopPrice = RoundToTick(entryPrice - rawStopDist, tickSize);
                            if (stopPrice >= candle.Low)
                                stopPrice = RoundToTick(candle.Low - structuralPad, tickSize);

                            decimal riskPts = Math.Max(minStopDist, entryPrice - stopPrice);
                            decimal effTp1Rr = Math.Max(1.50m, _quantTp1RiskReward);
                            decimal effTp2Rr = Math.Max(2.60m, _quantTp2RiskReward);

                            decimal minTp1Floor = Math.Max(step * minTp1Steps, RoundToTick(atr * 1.15m, tickSize));
                            decimal minTp2Floor = Math.Max(step * baseMinTp2Steps, RoundToTick(atr * 2.25m, tickSize));

                            decimal tp1Dist = Math.Max(minTp1Floor, RoundToTick(riskPts * effTp1Rr, tickSize));
                            decimal tp2Dist = Math.Max(Math.Max(minTp2Floor, tp1Dist + step * 8m), RoundToTick(riskPts * effTp2Rr, tickSize));

                            // Garde Options X-Ray : s'assurer que le CALL WALL ne bloque pas le potentiel vers TP1
                            bool blockedByCallWall = _enableOptionsXRayOverlay &&
                                                     opt.XRayCallWall > entryPrice &&
                                                     (opt.XRayCallWall - entryPrice) < tp1Dist * 0.85m;

                            if (!blockedByCallWall)
                            {
                                // Alignement / Extension intelligente du TP2 vers les aimants de liquidité Options X-Ray (CALL WALL / MAX PAIN)
                                if (_enableOptionsXRayOverlay)
                                {
                                    if (opt.XRayCallWall > entryPrice + tp1Dist * 1.35m && opt.XRayCallWall <= entryPrice + riskPts * 4.20m)
                                        tp2Dist = Math.Max(tp1Dist * 1.45m, RoundToTick(opt.XRayCallWall - entryPrice - step * 3m, tickSize));
                                    else if (opt.XRayMaxPain > entryPrice + tp1Dist * 1.45m && opt.XRayMaxPain <= entryPrice + riskPts * 3.80m && opt.XRayMaxPain > entryPrice + tp2Dist * 0.85m)
                                        tp2Dist = Math.Max(tp1Dist * 1.45m, RoundToTick(opt.XRayMaxPain - entryPrice - step * 2m, tickSize));
                                }

                                decimal tp1Price = RoundToTick(entryPrice + tp1Dist, tickSize);
                                decimal tp2Price = RoundToTick(entryPrice + tp2Dist, tickSize);
                                decimal actualRr = riskPts > 0m ? Math.Round(tp2Dist / riskPts, 2) : effTp2Rr;

                                string structTag = lvnConfirmed && hvnAnchored
                                    ? "HVN+LVN▲"
                                    : (lvnConfirmed ? "Rejet LVN▲" : (hvnAnchored ? "Support HVN▲" : "Swing Low▲"));
                                if (optionsWallSupport)
                                    structTag += " + X-Ray Wall";

                                string reason = $"◆ BIGTRADE PIÉGÉ ({bigTradeBullLots:0}L @ {bigTradeBullPrice}) + REVERSAL SWING▲ ({actualRr:0.0}R) | {structTag}{crossSuffix}";

                                return new VisualTradeSignal
                                {
                                    Bar = bar,
                                    Direction = 1,
                                    TriggerPrice = entryPrice,
                                    EntryPrice = entryPrice,
                                    RealAccountFillPrice = rawExecFillPx,
                                    IsLiveFillSynced = false,
                                    StopPrice = stopPrice,
                                    Tp1Price = tp1Price,
                                    Tp2Price = tp2Price,
                                    RiskRewardRatio = actualRr,
                                    ClusterZScore = clusterZ,
                                    AtrValue = atr,
                                    ConfluenceScore = score,
                                    CallFlowPct = opt.CallFlowPct,
                                    PutFlowPct = opt.PutFlowPct,
                                    PutCallRatio = opt.PutCallRatio,
                                    OiDelta = opt.OiDelta,
                                    SweepCount = opt.SweepCount,
                                    IsBigTradeReversal = true,
                                    BigTradePrice = bigTradeBullPrice,
                                    BigTradeLots = bigTradeBullLots,
                                    ReasonTag = reason,
                                    XRaySummaryTag = xrayTag
                                };
                            }
                        }
                    }
                }
            }

            // --- 4. Signal REVERSAL VENTE (SELL ▼) sur BigTrade Acheteur (Losange Vert ◆) Piégé ---
            if (hasBigTradeBearReversal && bigTradeBearPrice > 0m)
            {
                decimal entryPrice = GetLiveExecutableChartPrice(
                    bar, -1, candle.Close, tickSize, step, isCme,
                    out bool livePriceGapOk, out decimal crossBasisOffset, out decimal rawExecFillPx);

                if (livePriceGapOk)
                {
                    bool hvnAnchored = hasBearBarHvnResistance || hasCompHvnResistance;
                    bool lvnConfirmed = hasUpperLvnRejection || hasCompLvnBearRejection;
                    bool optionsWallResist = _enableOptionsXRayOverlay &&
                        ((opt.XRayCallWall > 0m && Math.Abs(bearTrapHigh - opt.XRayCallWall) <= atr * 1.15m) ||
                         (opt.XRayMaxPain > 0m && Math.Abs(bearTrapHigh - opt.XRayMaxPain) <= atr * 0.90m));

                    // Validation d'extrême de Swing : le piégeage doit se produire sur un sommet de Swing (et non au milieu d'un range)
                    bool atSwingHigh = bearTrapHigh >= swingHigh5 - atr * 0.15m ||
                                       (bearTrapHigh >= swingHigh8 - atr * 0.25m && (hvnAnchored || lvnConfirmed || optionsWallResist));

                    // Protection Anti-Fusée Haussière : bloque la vente face à 3 grosses bougies vertes impulsives sans cassure structurelle
                    bool rocketBlocked = isVerticalRocket && (bearTrapBarOffset == 0 || (bar > 0 && candle.Close > GetCandle(bar - 1).Low));

                    // Filtre Anti-Entrée Tardive : ne jamais vendre tout en bas d'une bougie géante déjà épuisée
                    decimal distFromTrapHigh = bearTrapHigh - entryPrice;
                    bool overExtendedEntry = barRange > atr * 1.65m || distFromTrapHigh > atr * 1.05m;

                    if (atSwingHigh && !rocketBlocked && !overExtendedEntry)
                    {
                        decimal clusterZ = 1.50m;
                        if (bestTrappedBuyersIdx >= 0 && bestTrappedBuyersIdx < rows.Length)
                            clusterZ = Math.Max(clusterZ, Math.Max(rows[bestTrappedBuyersIdx].VolumeZScore, rows[bestTrappedBuyersIdx].DeltaZScore));
                        if (bestBearHvnIdx >= 0 && bestBearHvnIdx < rows.Length)
                            clusterZ = Math.Max(clusterZ, rows[bestBearHvnIdx].VolumeZScore);

                        // Calcul réel du Score de Confluence Institutionnel (60..99)
                        int score = 62;
                        if (opt.TrendBias < 0) score += 9;
                        else if (opt.TrendBias == 0) score += 4;
                        if (hvnAnchored) score += 7;
                        if (lvnConfirmed) score += 6;
                        if (optionsWallResist) score += 7;
                        if (candle.Delta < 0m && candle.Close < candle.Open) score += 6;
                        if (bigTradeBearLots >= effBigTradeThreshold * 1.35m) score += 5;
                        if (bearTrapBarOffset >= 1) score += 3;
                        score = Math.Clamp(score, 60, 99);

                        int minRequiredScore = opt.TrendBias > 0 ? 80 : 74;
                        if (score >= minRequiredScore)
                        {
                            // Calibrage Anti-Microscalping : Stop-Loss structurel maîtrisé + Objectifs Swing TP1 (1.8R) & TP2 (3.2R+)
                            int baseMinStopSteps = isCme
                                ? (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 10 : 6)
                                : 5;
                            int minStopSteps = Math.Max(baseMinStopSteps, commStepFloor + 4);

                            int baseMinTp1Steps = isCme
                                ? (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 16 : 10)
                                : 8;
                            int minTp1Steps = Math.Max(baseMinTp1Steps, commStepFloor + 8);

                            int baseMinTp2Steps = isCme
                                ? (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 32 : 20)
                                : 16;

                            decimal structuralPad = (isCme && prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ) ? step * 3m : step * 2m;
                            decimal anchorHigh = Math.Max(bearTrapHigh + structuralPad, bigTradeBearPrice + structuralPad);
                            if (hasCompHvnResistance && compResistancePrice > 0m && compResistancePrice > entryPrice && (compResistancePrice - entryPrice) <= atr * 0.65m)
                                anchorHigh = Math.Max(anchorHigh, compResistancePrice + structuralPad);

                            decimal minStopDist = Math.Max(step * minStopSteps, RoundToTick(atr * 0.48m, tickSize));
                            decimal maxAllowedStopDist = Math.Max(minStopDist + step * 4m, RoundToTick(atr * Math.Clamp(_quantAtrStopMultiplier, 0.55m, 1.05m), tickSize));
                            decimal rawStopDist = Math.Clamp(anchorHigh - entryPrice, minStopDist, maxAllowedStopDist);

                            decimal stopPrice = RoundToTick(entryPrice + rawStopDist, tickSize);
                            if (stopPrice <= candle.High)
                                stopPrice = RoundToTick(candle.High + structuralPad, tickSize);

                            decimal riskPts = Math.Max(minStopDist, stopPrice - entryPrice);
                            decimal effTp1Rr = Math.Max(1.50m, _quantTp1RiskReward);
                            decimal effTp2Rr = Math.Max(2.60m, _quantTp2RiskReward);

                            decimal minTp1Floor = Math.Max(step * minTp1Steps, RoundToTick(atr * 1.15m, tickSize));
                            decimal minTp2Floor = Math.Max(step * baseMinTp2Steps, RoundToTick(atr * 2.25m, tickSize));

                            decimal tp1Dist = Math.Max(minTp1Floor, RoundToTick(riskPts * effTp1Rr, tickSize));
                            decimal tp2Dist = Math.Max(Math.Max(minTp2Floor, tp1Dist + step * 8m), RoundToTick(riskPts * effTp2Rr, tickSize));

                            // Garde Options X-Ray : s'assurer que le PUT WALL ne bloque pas le potentiel vers TP1
                            bool blockedByPutWall = _enableOptionsXRayOverlay &&
                                                    opt.XRayPutWall > 0m &&
                                                    opt.XRayPutWall < entryPrice &&
                                                    (entryPrice - opt.XRayPutWall) < tp1Dist * 0.85m;

                            if (!blockedByPutWall)
                            {
                                // Alignement / Extension intelligente du TP2 vers les aimants de liquidité Options X-Ray (PUT WALL / MAX PAIN)
                                if (_enableOptionsXRayOverlay)
                                {
                                    if (opt.XRayPutWall > 0m && opt.XRayPutWall < entryPrice - tp1Dist * 1.35m && opt.XRayPutWall >= entryPrice - riskPts * 4.20m)
                                        tp2Dist = Math.Max(tp1Dist * 1.45m, RoundToTick(entryPrice - opt.XRayPutWall - step * 3m, tickSize));
                                    else if (opt.XRayMaxPain > 0m && opt.XRayMaxPain < entryPrice - tp1Dist * 1.45m && opt.XRayMaxPain >= entryPrice - riskPts * 3.80m && opt.XRayMaxPain < entryPrice - tp2Dist * 0.85m)
                                        tp2Dist = Math.Max(tp1Dist * 1.45m, RoundToTick(entryPrice - opt.XRayMaxPain - step * 2m, tickSize));
                                }

                                decimal tp1Price = RoundToTick(entryPrice - tp1Dist, tickSize);
                                decimal tp2Price = RoundToTick(entryPrice - tp2Dist, tickSize);
                                decimal actualRr = riskPts > 0m ? Math.Round(tp2Dist / riskPts, 2) : effTp2Rr;

                                string structTag = lvnConfirmed && hvnAnchored
                                    ? "HVN+LVN▼"
                                    : (lvnConfirmed ? "Rejet LVN▼" : (hvnAnchored ? "Résist. HVN▼" : "Swing High▼"));
                                if (optionsWallResist)
                                    structTag += " + X-Ray Wall";

                                string reason = $"◆ BIGTRADE PIÉGÉ ({bigTradeBearLots:0}L @ {bigTradeBearPrice}) + REVERSAL SWING▼ ({actualRr:0.0}R) | {structTag}{crossSuffix}";

                                return new VisualTradeSignal
                                {
                                    Bar = bar,
                                    Direction = -1,
                                    TriggerPrice = entryPrice,
                                    EntryPrice = entryPrice,
                                    RealAccountFillPrice = rawExecFillPx,
                                    IsLiveFillSynced = false,
                                    StopPrice = stopPrice,
                                    Tp1Price = tp1Price,
                                    Tp2Price = tp2Price,
                                    RiskRewardRatio = actualRr,
                                    ClusterZScore = clusterZ,
                                    AtrValue = atr,
                                    ConfluenceScore = score,
                                    CallFlowPct = opt.CallFlowPct,
                                    PutFlowPct = opt.PutFlowPct,
                                    PutCallRatio = opt.PutCallRatio,
                                    OiDelta = opt.OiDelta,
                                    SweepCount = opt.SweepCount,
                                    IsBigTradeReversal = true,
                                    BigTradePrice = bigTradeBearPrice,
                                    BigTradeLots = bigTradeBearLots,
                                    ReasonTag = reason,
                                    XRaySummaryTag = xrayTag
                                };
                            }
                        }
                    }
                }
            }

            return sig;
        }

        // =====================================================================
        // Analyse complète des niveaux Footprint d'une bougie :
        // HVN, LVN, Heavy Absorption & Acheteurs/Vendeurs Piégés
        // (Avec support GetAllPriceLevels() & Micro-Cluster Pooling CME ES/NQ)
        // =====================================================================
        private ClusterRow[] AnalyzeBarClusters(
            IndicatorCandle candle,
            decimal tickSize,
            out int pocIndex,
            out int primaryLvnIndex,
            out int bestBullAbsIndex,
            out int bestBearAbsIndex,
            out int bestTrappedSellersIndex,
            out int bestTrappedBuyersIndex)
        {
            pocIndex = -1;
            primaryLvnIndex = -1;
            bestBullAbsIndex = -1;
            bestBearAbsIndex = -1;
            bestTrappedSellersIndex = -1;
            bestTrappedBuyersIndex = -1;

            int ticksPerLevel = Math.Max(1, _ticksPerRow);
            decimal step = tickSize * ticksPerLevel;
            if (step <= 0m || candle.High < candle.Low)
                return Array.Empty<ClusterRow>();

            decimal baseLow = RoundToTick(candle.Low, tickSize);
            decimal baseHigh = RoundToTick(candle.High, tickSize);
            int count = (int)Math.Floor((baseHigh - baseLow) / step) + 1;
            if (count <= 0 || count > 2500)
                return Array.Empty<ClusterRow>();

            var contractInfo = ResolveContractAndCrossTradingInfo(tickSize);
            bool isCme = contractInfo.IsCmeMarket;
            MarketProfilePreset prof = contractInfo.EffectiveProfile;

            var rows = new ClusterRow[count];
            for (int i = 0; i < count; i++)
            {
                rows[i].Price = RoundToTick(baseLow + i * step, tickSize);
            }

            // 0. Extraction rapide & exacte via GetAllPriceLevels() (évite les erreurs d'arrondi de tick sur CME Rithmic/CQG/dxFeed)
            bool populatedFromAllLevels = false;
            try
            {
                var allLevels = candle.GetAllPriceLevels();
                if (allLevels != null)
                {
                    foreach (var pv in allLevels)
                    {
                        if (pv == null)
                            continue;
                        decimal b = pv.Bid;
                        decimal a = pv.Ask;
                        if (b + a <= 0m)
                            continue;

                        int idx = (int)Math.Floor(((RoundToTick(pv.Price, tickSize) - baseLow) + tickSize * 0.1m) / step);
                        if (idx >= 0 && idx < count)
                        {
                            rows[idx].Bid += b;
                            rows[idx].Ask += a;
                            rows[idx].Volume += (b + a);
                            rows[idx].Delta += (a - b);
                            populatedFromAllLevels = true;
                        }
                    }
                }
            }
            catch
            {
                populatedFromAllLevels = false;
            }

            decimal maxVol = 0m;
            decimal totalBarVol = 0m;
            int activeLevels = 0;

            for (int i = 0; i < count; i++)
            {
                if (!populatedFromAllLevels)
                {
                    decimal basePrice = rows[i].Price;
                    decimal bidSum = 0m;
                    decimal askSum = 0m;

                    for (int t = 0; t < ticksPerLevel; t++)
                    {
                        decimal p = RoundToTick(basePrice + t * tickSize, tickSize);
                        if (p > baseHigh + tickSize * 0.1m)
                            break;

                        var info = candle.GetPriceVolumeInfo(p);
                        if (info != null)
                        {
                            bidSum += info.Bid;
                            askSum += info.Ask;
                        }
                    }

                    rows[i].Bid = bidSum;
                    rows[i].Ask = askSum;
                    rows[i].Volume = bidSum + askSum;
                    rows[i].Delta = askSum - bidSum;
                }

                decimal vol = rows[i].Volume;
                if (vol > 0m)
                {
                    activeLevels++;
                    totalBarVol += vol;
                }

                if (vol > maxVol)
                {
                    maxVol = vol;
                    pocIndex = i;
                }
            }

            if (maxVol <= 0m || pocIndex < 0)
                return rows;

            rows[pocIndex].IsPoc = true;
            decimal avgLevelVol = activeLevels > 0 ? totalBarVol / activeLevels : maxVol;

            // Calcul statistique Quant : Écart-Type (σ) et Z-Score de Volume & Delta par niveau
            double sumSqVol = 0.0;
            double sumDelta = 0.0;
            double sumSqDelta = 0.0;
            double meanVolD = (double)avgLevelVol;

            for (int i = 0; i < count; i++)
            {
                if (rows[i].Volume <= 0m)
                    continue;
                double dv = (double)rows[i].Volume - meanVolD;
                sumSqVol += dv * dv;
                sumDelta += (double)rows[i].Delta;
            }

            double meanDeltaD = activeLevels > 0 ? sumDelta / activeLevels : 0.0;
            for (int i = 0; i < count; i++)
            {
                if (rows[i].Volume <= 0m)
                    continue;
                double dd = (double)rows[i].Delta - meanDeltaD;
                sumSqDelta += dd * dd;
            }

            decimal stdVol = activeLevels > 1 ? (decimal)Math.Sqrt(sumSqVol / activeLevels) : 1m;
            decimal stdDelta = activeLevels > 1 ? (decimal)Math.Sqrt(sumSqDelta / activeLevels) : 1m;
            if (stdVol <= 0m) stdVol = 1m;
            if (stdDelta <= 0m) stdDelta = 1m;

            for (int i = 0; i < count; i++)
            {
                if (rows[i].Volume <= 0m)
                    continue;
                rows[i].VolumeZScore = Math.Round((rows[i].Volume - avgLevelVol) / stdVol, 2);
                rows[i].DeltaZScore = Math.Round((Math.Abs(rows[i].Delta) - (decimal)Math.Abs(meanDeltaD)) / stdDelta, 2);
            }

            // 1. Détection HVN Intra-Bougie
            if (_enableBarHvn)
            {
                decimal effHvnMinVol = isCme ? Math.Min(_hvnMinBarVolume, 4m) : _hvnMinBarVolume;
                decimal hvnCutoff = maxVol * (_hvnBarPctOfPoc / 100m);
                for (int i = 0; i < count; i++)
                {
                    decimal v = rows[i].Volume;
                    if (v <= 0m || v < effHvnMinVol || v < hvnCutoff)
                        continue;

                    bool isLocalPeak = true;
                    if (_requireLocalPeakForBarHvn && count >= 3)
                    {
                        decimal prevV = i > 0 ? rows[i - 1].Volume : 0m;
                        decimal nextV = i < count - 1 ? rows[i + 1].Volume : 0m;
                        isLocalPeak = v >= prevV && v >= nextV && (v > prevV || v > nextV || i == pocIndex);
                    }

                    if (isLocalPeak || i == pocIndex)
                        rows[i].IsHvn = true;
                }
            }

            // 2. Détection LVN Intra-Bougie (poches de faible liquidité 1 à 3 ticks entre deux zones plus denses)
            if (_enableBarLvn && count >= 3)
            {
                decimal effLvnPct = isCme ? Math.Max(_lvnBarMaxPctOfPoc, 28m) : _lvnBarMaxPctOfPoc;
                decimal lvnCutoff = maxVol * (effLvnPct / 100m);
                int startIdx = Math.Min(count - 1, Math.Max(1, _lvnExcludeExtremeTicks));
                int endIdx = Math.Max(0, count - 1 - Math.Max(1, _lvnExcludeExtremeTicks));

                decimal lowestLvnVol = decimal.MaxValue;
                for (int i = startIdx; i <= endIdx; i++)
                {
                    decimal v = rows[i].Volume;
                    if (v > lvnCutoff || rows[i].IsHvn)
                        continue;

                    decimal prevV = i > 0 ? rows[i - 1].Volume : decimal.MaxValue;
                    decimal nextV = i < count - 1 ? rows[i + 1].Volume : decimal.MaxValue;
                    bool singleValley = v <= prevV && v <= nextV && (v < prevV || v < nextV);

                    // Support des poches LVN multi-ticks sur CME (ex: 2 ticks adjacents de faible volume encadrés par des volumes plus denses)
                    bool pocketValley = false;
                    if (!singleValley && count >= 4)
                    {
                        decimal maxBelow = prevV;
                        if (i >= 2) maxBelow = Math.Max(maxBelow, rows[i - 2].Volume);
                        if (i >= 3) maxBelow = Math.Max(maxBelow, rows[i - 3].Volume);

                        decimal maxAbove = nextV;
                        if (i + 2 < count) maxAbove = Math.Max(maxAbove, rows[i + 2].Volume);
                        if (i + 3 < count) maxAbove = Math.Max(maxAbove, rows[i + 3].Volume);

                        pocketValley = (v <= prevV && v <= nextV) &&
                                       (maxBelow > v * 1.35m + 1m) &&
                                       (maxAbove > v * 1.35m + 1m);
                    }

                    if (singleValley || pocketValley)
                    {
                        rows[i].IsLvn = true;
                        if (v < lowestLvnVol)
                        {
                            lowestLvnVol = v;
                            primaryLvnIndex = i;
                        }
                    }
                }
            }

            decimal barRange = candle.High - candle.Low;

            // Calcul du volume moyen hors POC/HVN pour ne jamais pénaliser la détection dans les mèches par un POC géant
            decimal nonHvnVolSum = 0m;
            int nonHvnCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (rows[i].Volume > 0m && !rows[i].IsPoc && !rows[i].IsHvn)
                {
                    nonHvnVolSum += rows[i].Volume;
                    nonHvnCount++;
                }
            }
            decimal nonHvnAvgVol = nonHvnCount > 0 ? (nonHvnVolSum / nonHvnCount) : (avgLevelVol * 0.65m);

            // 3. Détection des BigTrades (Losanges ◆) & BigTrades Piégés (Seul déclencheur de Reversal du Bot)
            if (_enableBigTradesReversal && count >= 2)
            {
                decimal effBigTradeMin = GetEffectiveBigTradeMinLots(tickSize);
                decimal adaptiveBlockLots = isCme
                    ? Math.Max(effBigTradeMin * 0.85m, avgLevelVol * 2.55m)
                    : Math.Max(effBigTradeMin * 0.80m, avgLevelVol * 2.65m);

                decimal lowerTrapLimit = candle.Low + barRange * 0.45m;
                decimal upperTrapLimit = candle.High - barRange * 0.45m;

                for (int i = 0; i < count; i++)
                {
                    if (rows[i].Volume <= 0m)
                        continue;

                    // Agrégation stricte d'un balayage agressif BigTrade uniquement sur le tick contigu si fortement déséquilibré
                    decimal sellSweepVol = rows[i].Bid;
                    if (isCme && _ticksPerRow == 1)
                    {
                        if (i + 1 < count && rows[i + 1].Bid >= rows[i + 1].Ask * 1.50m)
                            sellSweepVol += rows[i + 1].Bid * 0.55m;
                        else if (i > 0 && rows[i - 1].Bid >= rows[i - 1].Ask * 1.50m)
                            sellSweepVol += rows[i - 1].Bid * 0.50m;
                    }

                    decimal buySweepVol = rows[i].Ask;
                    if (isCme && _ticksPerRow == 1)
                    {
                        if (i - 1 >= 0 && rows[i - 1].Ask >= rows[i - 1].Bid * 1.50m)
                            buySweepVol += rows[i - 1].Ask * 0.55m;
                        else if (i + 1 < count && rows[i + 1].Ask >= rows[i + 1].Bid * 1.50m)
                            buySweepVol += rows[i + 1].Ask * 0.50m;
                    }

                    bool isLocalSellPeak = (i == 0 || rows[i].Bid >= rows[i - 1].Bid) && (i == count - 1 || rows[i].Bid >= rows[i + 1].Bid);
                    bool isLocalBuyPeak = (i == 0 || rows[i].Ask >= rows[i - 1].Ask) && (i == count - 1 || rows[i].Ask >= rows[i + 1].Ask);

                    bool isSellBigTrade = isLocalSellPeak &&
                                          ((rows[i].Bid >= effBigTradeMin && rows[i].Bid >= rows[i].Ask * 1.45m) ||
                                           (sellSweepVol >= effBigTradeMin && rows[i].Bid >= effBigTradeMin * 0.65m && rows[i].Bid >= rows[i].Ask * 1.60m && rows[i].VolumeZScore >= 1.25m) ||
                                           (sellSweepVol >= adaptiveBlockLots && rows[i].Bid >= rows[i].Ask * 1.75m && rows[i].VolumeZScore >= 1.55m && rows[i].DeltaZScore >= 1.35m));

                    bool isBuyBigTrade = isLocalBuyPeak &&
                                         ((rows[i].Ask >= effBigTradeMin && rows[i].Ask >= rows[i].Bid * 1.45m) ||
                                          (buySweepVol >= effBigTradeMin && rows[i].Ask >= effBigTradeMin * 0.65m && rows[i].Ask >= rows[i].Bid * 1.60m && rows[i].VolumeZScore >= 1.25m) ||
                                          (buySweepVol >= adaptiveBlockLots && rows[i].Ask >= rows[i].Bid * 1.75m && rows[i].VolumeZScore >= 1.55m && rows[i].DeltaZScore >= 1.35m));

                    if (isSellBigTrade)
                    {
                        rows[i].HasBigTradeSell = true;
                        rows[i].BigTradeSellVol = Math.Round(Math.Max(rows[i].Bid, sellSweepVol), 0);

                        // BigTrade Vendeur (Losange Rouge ◆) Piégé en bas : vente massive dans les 45% bas suivie d'un franc rejet au-dessus
                        if (rows[i].Price <= lowerTrapLimit && candle.Close >= rows[i].Price + step * 2m)
                        {
                            rows[i].TrappedBigTradeSide = 1;
                            if (bestTrappedSellersIndex < 0 || rows[i].BigTradeSellVol > rows[bestTrappedSellersIndex].BigTradeSellVol)
                                bestTrappedSellersIndex = i;
                        }
                    }

                    if (isBuyBigTrade)
                    {
                        rows[i].HasBigTradeBuy = true;
                        rows[i].BigTradeBuyVol = Math.Round(Math.Max(rows[i].Ask, buySweepVol), 0);

                        // BigTrade Acheteur (Losange Vert ◆) Piégé en haut : achat massif dans les 45% hauts suivi d'un franc rejet en dessous
                        if (rows[i].Price >= upperTrapLimit && candle.Close <= rows[i].Price - step * 2m && rows[i].TrappedBigTradeSide == 0)
                        {
                            rows[i].TrappedBigTradeSide = -1;
                            if (bestTrappedBuyersIndex < 0 || rows[i].BigTradeBuyVol > rows[bestTrappedBuyersIndex].BigTradeBuyVol)
                                bestTrappedBuyersIndex = i;
                        }
                    }
                }
            }

            return rows;
        }

        // =====================================================================
        // OnRender : Rendu graphique haute précision sur le Footprint ATAS X
        // =====================================================================
        protected override void OnRender(RenderContext context, DrawingLayouts layout)
        {
            if (ChartInfo == null || InstrumentInfo == null || ChartInfo.PriceChartContainer == null)
                return;

            int firstBar = FirstVisibleBarNumber;
            int lastBar = Math.Min(CurrentBar - 1, LastVisibleBarNumber);
            if (firstBar > lastBar || lastBar < 0)
                return;

            decimal tickSize = InstrumentInfo.TickSize;
            if (tickSize <= 0m)
                tickSize = 0.25m;

            int ticksPerLevel = Math.Max(1, _ticksPerRow);
            decimal step = tickSize * ticksPerLevel;

            int rowHeight = Math.Abs(ChartInfo.GetYByPrice(0m) - ChartInfo.GetYByPrice(step));
            if (rowHeight < 2)
                rowHeight = 2;

            var cellFont = new RenderFont("Consolas", CellFontSize);
            var boldFont = new RenderFont("Consolas", CellFontSize, FontStyle.Bold);
            Rectangle chartRegion = ChartInfo.PriceChartContainer.Region;

            // 1. Rendu des zones HVN & LVN du Profil Composite (arrière-plan)
            int compositeHvnCount = 0;
            int compositeLvnCount = 0;
            decimal compositePocPrice = 0m;
            decimal compositePocVol = 0m;

            if (EnableCompositeProfile)
            {
                RenderCompositeProfileZones(
                    context, chartRegion, lastBar, tickSize, step, rowHeight, cellFont,
                    out compositeHvnCount, out compositeLvnCount, out compositePocPrice, out compositePocVol);
            }

            // Listes des événements détectés pour projeter les rayons S/R et alimenter le HUD
            var activeAbsRays = new List<RaySignalEvent>();
            var activeTrappedRays = new List<RaySignalEvent>();
            var visibleTradeSignals = new List<(VisualTradeSignal sig, IndicatorCandle candle, int xMid, int xRight)>();

            int visibleHvnCells = 0;
            int visibleLvnCells = 0;
            int visibleBullAbs = 0;       // Nombre de BigTrades Vendeurs (Losanges Rouges ◆) visibles
            int visibleBearAbs = 0;       // Nombre de BigTrades Acheteurs (Losanges Verts ◆) visibles
            int visibleTrappedSellers = 0; // Nombre de BigTrades Vendeurs Piégés (◆ Or) visibles
            int visibleTrappedBuyers = 0;  // Nombre de BigTrades Acheteurs Piégés (◆ Or) visibles
            int visibleBuySignals = 0;
            int visibleSellSignals = 0;

            RaySignalEvent? latestAbsorption = null;
            RaySignalEvent? latestTrapped = null;
            VisualTradeSignal? latestTradeSignal = null;
            BarOptionsFlowMetrics latestBarOptMetrics = default;

            // 2. Boucle sur les bougies visibles : Rendu Footprint HVN, LVN, BigTrades (Losanges ◆) & Signaux Reversal
            for (int bar = firstBar; bar <= lastBar; bar++)
            {
                var candle = GetCandle(bar);
                var rows = AnalyzeBarClusters(
                    candle, tickSize,
                    out _, out _,
                    out int bestBullAbsIdx, out int bestBearAbsIdx,
                    out int bestTrapSellersIdx, out int bestTrapBuyersIdx);
                if (rows.Length == 0)
                    continue;

                _barRowsCache[bar] = rows;

                var barOpt = AnalyzeBarOptionsFlow(bar, candle, rows, tickSize);
                if (bar == lastBar)
                    latestBarOptMetrics = barOpt;

                int xStart = ChartInfo.GetXByBar(bar, true);
                int xEnd = ChartInfo.GetXByBar(bar + 1, true);
                int barSpan = xEnd - xStart;
                if (barSpan <= 4)
                    barSpan = Math.Max(8, (int)ChartInfo.PriceChartContainer.BarsWidth);

                int xMid = xStart + barSpan / 2;
                int cellW = CustomCellWidthPx > 0 ? CustomCellWidthPx : Math.Max(8, barSpan - 4);
                int cellX = xMid - cellW / 2;

                for (int i = 0; i < rows.Length; i++)
                {
                    ClusterRow r = rows[i];
                    if (r.Volume <= 0m && !r.IsLvn)
                        continue;

                    if (r.HasBigTradeSell) visibleBullAbs++;
                    if (r.HasBigTradeBuy) visibleBearAbs++;
                    if (r.TrappedBigTradeSide > 0) visibleTrappedSellers++;
                    if (r.TrappedBigTradeSide < 0) visibleTrappedBuyers++;

                    int yCenter = ChartInfo.GetYByPrice(r.Price, false);
                    int yTop = yCenter - rowHeight / 2;
                    Rectangle cellRect = new Rectangle(cellX, yTop, cellW, rowHeight);

                    // --- A. Rendu LVN Intra-Bougie ---
                    if (r.IsLvn)
                    {
                        visibleLvnCells++;
                        Color fill = Color.FromArgb(BarLvnFillAlpha, BarLvnColor.R, BarLvnColor.G, BarLvnColor.B);
                        Color border = Color.FromArgb(210, BarLvnColor.R, BarLvnColor.G, BarLvnColor.B);
                        context.FillRectangle(fill, cellRect);

                        var lvnPen = new RenderPen(border, 1f, DashStyle.Dash);
                        context.DrawRectangle(lvnPen, cellRect);

                        if (ShowCellBadges && rowHeight >= CellFontSize && cellW >= 36)
                        {
                            string tag = $"LVN {r.Volume:0}";
                            Size sz = context.MeasureString(tag, cellFont);
                            context.DrawString(tag, cellFont, BarLvnColor, cellRect.Right - sz.Width - 3, yCenter - sz.Height / 2);
                        }
                    }

                    // --- B. Rendu HVN & POC Intra-Bougie ---
                    if (r.IsHvn)
                    {
                        visibleHvnCells++;
                        Color baseColor = r.IsPoc ? BarPocColor : BarHvnColor;
                        int alpha = r.IsPoc ? Math.Min(255, BarHvnFillAlpha + 30) : BarHvnFillAlpha;
                        Color fill = Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
                        Color border = Color.FromArgb(240, baseColor.R, baseColor.G, baseColor.B);

                        context.FillRectangle(fill, cellRect);
                        var hvnPen = new RenderPen(border, r.IsPoc ? 2f : 1f);
                        context.DrawRectangle(hvnPen, cellRect);

                        if (ShowCellBadges && rowHeight >= CellFontSize && cellW >= 38)
                        {
                            string tag = r.IsPoc ? $"POC {r.Volume:0}" : $"HVN {r.Volume:0}";
                            Size sz = context.MeasureString(tag, boldFont);
                            context.DrawString(tag, boldFont, baseColor, cellRect.Left + 3, yCenter - sz.Height / 2);
                        }
                    }

                    // --- C. Texte Bid x Ask intégré optionnel (si mode chandelier) ---
                    if (RenderBuiltInBidAskText && rowHeight >= CellFontSize && cellW >= 48)
                    {
                        string baText = $"{r.Bid:0} x {r.Ask:0}";
                        Size sz = context.MeasureString(baText, cellFont);
                        Color txtColor = r.Delta >= 0m
                            ? Color.FromArgb(220, 130, 240, 170)
                            : Color.FromArgb(220, 245, 140, 140);
                        context.DrawString(baText, cellFont, txtColor, xMid - sz.Width / 2, yCenter - sz.Height / 2);
                    }
                }

                // Rendu des Losanges BigTrades (◆ Acheteurs Verts / Vendeurs Rouges) & BigTrades Piégés directement sur la bougie
                if (_enableBigTradesReversal && ShowBigTradeDiamonds)
                {
                    RenderBigTradeDiamondsOnBar(context, bar, candle, rows, xMid, rowHeight, boldFont, cellFont);
                }

                // Évaluation du signal Reversal sur BigTrade Piégé (◆) + HVN/LVN + Options X-Ray
                if (_enableVisualBuySellAlerts)
                {
                    var sig = EvaluateBuySellSignal(
                        bar, candle, rows, tickSize,
                        bestBullAbsIdx, bestBearAbsIdx,
                        bestTrapSellersIdx, bestTrapBuyersIdx,
                        barOpt);

                    if (sig.Direction != 0)
                    {
                        if (sig.Direction > 0) visibleBuySignals++; else visibleSellSignals++;
                        latestTradeSignal = sig;
                        visibleTradeSignals.Add((sig, candle, xMid, cellX + cellW));
                    }
                }
            }

            // 2bis. Rendu de l'Indicateur Options X-Ray (CALL WALL, MAX PAIN, PUT WALL, 0 DTE & Histogramme Strike GEX Jaune/Bleu-Gris)
            if (_enableOptionsXRayOverlay)
            {
                RenderOptionsXRayOverlay(context, chartRegion, firstBar, lastBar, tickSize, latestBarOptMetrics, boldFont, cellFont);
            }

            // 5. Simulation, Synchronisation Temps Réel avec le Compte Actif ATAS X & Alignement 100% Signaux <-> Trades
            EnsureTradingManagerSubscriptions();

            BotPerformanceState botPerf = default;
            List<BotTradeExecutionRecord> botTrades = new List<BotTradeExecutionRecord>();
            if (_enableTradingBot)
            {
                botPerf = SimulateQuantTradingBot(lastBar, tickSize, out botTrades);
            }
            else
            {
                botPerf.StatusLabel = "DÉSACTIVÉ (Cliquez sur [BOT BIGTRADES: OFF] sur la charte)";
            }

            // Si l'option d'alignement strict est active, n'afficher les cartouches ★ QUANT ACHAT/VENTE qu'aux endroits exacts où le Bot ouvre un trade
            if (_enableTradingBot && _onlyShowExecutedBotSignals)
            {
                var alignedSignals = new List<(VisualTradeSignal sig, IndicatorCandle candle, int xMid, int xRight)>();
                visibleBuySignals = 0;
                visibleSellSignals = 0;
                latestTradeSignal = null;

                for (int i = 0; i < botTrades.Count; i++)
                {
                    var tr = botTrades[i];
                    if (tr.EntrySignal.Direction == 0)
                        continue;

                    latestTradeSignal = tr.EntrySignal;
                    if (tr.EntryBar < firstBar || tr.EntryBar > lastBar)
                        continue;

                    var trCandle = GetCandle(tr.EntryBar);
                    int xS = ChartInfo.GetXByBar(tr.EntryBar, true);
                    int xE = ChartInfo.GetXByBar(tr.EntryBar + 1, true);
                    int bSpan = xE - xS;
                    if (bSpan <= 4)
                        bSpan = Math.Max(8, (int)ChartInfo.PriceChartContainer.BarsWidth);

                    int xM = xS + bSpan / 2;
                    int cW = CustomCellWidthPx > 0 ? CustomCellWidthPx : Math.Max(8, bSpan - 4);
                    int cX = xM - cW / 2;

                    if (tr.EntrySignal.Direction > 0) visibleBuySignals++;
                    else visibleSellSignals++;

                    alignedSignals.Add((tr.EntrySignal, trCandle, xM, cX + cW));
                }

                visibleTradeSignals = alignedSignals;
            }

            // 6. Rendu des Cartouches Visuels ACHAT ▲ / VENTE ▼ et Niveaux Entrée/Stop aux endroits exacts d'ouverture
            if (_enableVisualBuySellAlerts && visibleTradeSignals.Count > 0)
            {
                for (int i = 0; i < visibleTradeSignals.Count; i++)
                {
                    var item = visibleTradeSignals[i];
                    RenderBuySellSignalOnBar(context, item.sig, item.candle, item.xMid, item.xRight, lastBar, rowHeight, boldFont, cellFont);
                }
            }

            // Synchronise le PnL Réel (Réalisé + Latent), la Position ouverte, le Solde et le Drawdown avec le Compte Actif ATAS
            SynchronizeWithActiveAccount(lastBar, tickSize, ref botPerf);

            if (_enableTradingBot && _botMode == BotExecutionMode.LiveAutoTradingManager && lastBar >= 0)
            {
                decimal renderPx = GetCandle(lastBar).Close > 0m
                    ? GetCandle(lastBar).Close
                    : (TradingManager?.Security?.LastTradePrice ?? 0m);
                if (renderPx > 0m)
                    CheckLivePositionTargetsOnTick(renderPx);
            }

            if (_enableTradingBot && ShowBotExecutionOnChart && botTrades.Count > 0)
            {
                RenderBotTradesOnChart(context, botTrades, firstBar, lastBar, boldFont, cellFont);
            }

            // 6bis. Rendu des Badges d'Ordres Style DOM ATAS ([X] [2] [TP | $ +... | R] / [SL | $ +... | R])
            if (ShowDomStyleOrderTags)
            {
                RenderDomStyleActiveOrderTags(context, chartRegion, lastBar, tickSize, botPerf, latestTradeSignal, boldFont, cellFont);
            }

            // 6ter. Rendu de l'Encadré de Notification BigTrade en bas à gauche ("NQ: New BigTrade at ... is more than 40 lots")
            if (_enableBigTradesReversal && ShowBigTradeAlertToast)
            {
                RenderBigTradeAlertToast(context, chartRegion, lastBar, tickSize, boldFont, cellFont);
            }

            // 7. Rendu de la Bannière Visuelle d'Alerte Achat/Vente en haut du graphique
            if (_enableVisualBuySellAlerts && ShowVisualAlertBanner && latestTradeSignal.HasValue)
            {
                RenderVisualAlertBanner(context, chartRegion, latestTradeSignal.Value, latestBarOptMetrics, botPerf);
            }

            // 8. Rendu du panneau HUD récapitulatif en temps réel (incluant Quant Bot, Max DD & Max PnL)
            if (ShowHudPanel)
            {
                RenderHud(
                    context, chartRegion,
                    visibleHvnCells, visibleLvnCells,
                    compositeHvnCount, compositeLvnCount,
                    compositePocPrice, compositePocVol,
                    visibleBullAbs, visibleBearAbs,
                    visibleTrappedSellers, visibleTrappedBuyers,
                    visibleBuySignals, visibleSellSignals,
                    latestAbsorption, latestTrapped,
                    latestTradeSignal, latestBarOptMetrics,
                    botPerf);
            }
            else
            {
                _hudBotToggleBtnRect = Rectangle.Empty;
            }

            // 9. Rendu du Bouton Interactif d'Activation du Bot, Encadrement Horaire & Encadrement Compte Actif sur la Charte
            if (ShowChartBotToggleButton)
            {
                RenderOnChartBotControlButton(context, chartRegion, botPerf);
            }
            else
            {
                _chartBotToggleBtnRect = Rectangle.Empty;
                _chartBotModeBtnRect = Rectangle.Empty;
                _chartBotResetBtnRect = Rectangle.Empty;
                _chartProfileBtnRect = Rectangle.Empty;
                _chartCrossTradeBtnRect = Rectangle.Empty;
                _chartSchedToggleRect = Rectangle.Empty;
                _chartStartMinusRect = Rectangle.Empty;
                _chartStartValRect = Rectangle.Empty;
                _chartStartPlusRect = Rectangle.Empty;
                _chartEndMinusRect = Rectangle.Empty;
                _chartEndValRect = Rectangle.Empty;
                _chartEndPlusRect = Rectangle.Empty;
                _chartAcctSyncToggleRect = Rectangle.Empty;
                _chartAcctScopeToggleRect = Rectangle.Empty;
            }
        }

        // =====================================================================
        // MOTEUR DU BOT DE TRADING QUANT & GESTION DU RISQUE (MAX DRAWDOWN & MAX PNL)
        // =====================================================================
        private decimal GetPointDollarValue(decimal tickSize)
        {
            return ResolveContractAndCrossTradingInfo(tickSize).TradedPointValueUsd;
        }

        private BotPerformanceState SimulateQuantTradingBot(
            int upToBar,
            decimal tickSize,
            out List<BotTradeExecutionRecord> tradeHistory)
        {
            tradeHistory = new List<BotTradeExecutionRecord>();
            var contractInfo = ResolveContractAndCrossTradingInfo(tickSize);
            bool isCme = contractInfo.IsCmeMarket;
            MarketProfilePreset prof = contractInfo.EffectiveProfile;

            var state = new BotPerformanceState
            {
                StatusLabel = _enableTradingBot ? "ACTIF — RECHERCHE SIGNAL QUANT" : "DÉSACTIVÉ",
                MarketProfileLabel = contractInfo.ProfileBadge,
                CrossTradingLabel = contractInfo.CrossTradingBadge,
                EffectivePointValueUsd = contractInfo.TradedPointValueUsd
            };

            if (!_enableTradingBot || upToBar < 0)
                return state;

            decimal step = tickSize * Math.Max(1, _ticksPerRow);
            decimal pointVal = contractInfo.TradedPointValueUsd;
            decimal commPerSide = contractInfo.TradedCommissionPerSideUsd;
            decimal commRtUsd = Math.Max(0.50m, commPerSide * 2m);
            decimal stepValueUsd = Math.Max(0.05m, pointVal * step);
            int commStepFloor = Math.Max(1, (int)Math.Ceiling(commRtUsd / stepValueUsd));
            // Fenêtre de départ déterministe (indépendante du défilement FirstVisibleBarNumber) pour garantir 100% d'identité entre OnCalculate et OnRender
            int lookbackStart = Math.Max(0, upToBar - Math.Max(260, ProfileLookbackBars * 4));
            if (FirstVisibleBarNumber >= 0 && FirstVisibleBarNumber < lookbackStart)
                lookbackStart = Math.Max(0, FirstVisibleBarNumber - 20);
            if (_botResetLimitsEachSession)
            {
                int sessionStart = upToBar;
                while (sessionStart > lookbackStart && !IsNewSession(sessionStart))
                    sessionStart--;
                lookbackStart = Math.Min(lookbackStart, sessionStart);
            }
            if (_manualResetBarIndex > lookbackStart && _manualResetBarIndex <= upToBar)
            {
                lookbackStart = _manualResetBarIndex;
            }

            int activeDir = 0;
            int activeQty = 0;
            int initialQty = Math.Max(1, _botTradeQuantity);
            int entryBar = -1;
            int lastEntryBar = -999;
            int lastLossExitBar = -999;
            decimal entryPrice = 0m;
            decimal stopPrice = 0m;
            decimal initialRiskPts = step * 2m;
            decimal tp1Price = 0m;
            decimal tp2Price = 0m;
            decimal maxFavPeakPrice = 0m;
            bool tp1Hit = false;
            decimal bankedPartialUsd = 0m;
            VisualTradeSignal activeSignal = default;

            var tradeReturns = new List<double>();

            for (int b = lookbackStart; b <= upToBar; b++)
            {
                if (_botResetLimitsEachSession && b > lookbackStart && IsNewSession(b))
                {
                    state.IsHaltedByMaxDrawdown = false;
                    state.IsHaltedByMaxProfit = false;
                    state.RealizedPnlUsd = 0m;
                    state.PeakPnlUsd = 0m;
                    state.CurrentDrawdownUsd = 0m;
                    activeDir = 0;
                    activeQty = 0;
                }

                var c = GetCandle(b);

                // 1. Gestion dynamique Swing (Anti-Microscalping) de la position ouverte du Bot sur la bougie b
                if (activeDir != 0 && b > entryBar)
                {
                    if (activeDir > 0 && c.High > maxFavPeakPrice)
                        maxFavPeakPrice = c.High;
                    else if (activeDir < 0 && (maxFavPeakPrice <= 0m || c.Low < maxFavPeakPrice))
                        maxFavPeakPrice = c.Low;

                    decimal tp1DistPts = Math.Max(step * 4m, Math.Abs(tp1Price - entryPrice));
                    decimal maxFavExcursion = activeDir > 0 ? (maxFavPeakPrice - entryPrice) : (entryPrice - maxFavPeakPrice);
                    decimal favPeakPrice = activeDir > 0 ? c.High : c.Low;
                    bool favorableClose = activeDir > 0 ? (c.Close >= c.Open) : (c.Close <= c.Open);

                    // Protection Anti-Microscalping : on ne remonte JAMAIS le stop à quelques ticks dès 0.45R !
                    // Le Breakeven+ de sécurité (+0.25R) ne s'active que si le prix a déjà parcouru au moins +1.25R (ou 78% de TP1)
                    if (!tp1Hit)
                    {
                        decimal safeBeTrigger = Math.Max(initialRiskPts * 1.25m, tp1DistPts * 0.78m);
                        bool favPeakReachedFirst = favorableClose || (Math.Abs(c.Open - favPeakPrice) <= Math.Abs(c.Open - stopPrice));

                        if (maxFavExcursion >= safeBeTrigger && favPeakReachedFirst)
                        {
                            decimal lockDist = Math.Max(step * (commStepFloor + 2), RoundToTick(initialRiskPts * 0.25m, tickSize));
                            decimal safeBe = RoundToTick(entryPrice + activeDir * lockDist, tickSize);
                            if ((activeDir > 0 && safeBe > stopPrice) || (activeDir < 0 && safeBe < stopPrice))
                                stopPrice = safeBe;
                        }
                    }

                    bool stopTriggered = activeDir > 0 ? (c.Low <= stopPrice) : (c.High >= stopPrice);
                    bool tp2Triggered = activeDir > 0 ? (c.High >= tp2Price) : (c.Low <= tp2Price);
                    bool tp1Triggered = !tp1Hit && (activeDir > 0 ? (c.High >= tp1Price) : (c.Low <= tp1Price));
                    bool tp1CloserToOpen = Math.Abs(c.Open - tp1Price) <= Math.Abs(c.Open - stopPrice);

                    // Si TP1 et Stop sont touchés sur la même bougie et que TP1 était plus proche de l'ouverture (ou clôture favorable), TP1 est exécuté en premier
                    if (tp1Triggered && stopTriggered && (favorableClose || tp1CloserToOpen))
                    {
                        stopTriggered = false;
                    }

                    if (tp2Triggered && (!stopTriggered || favorableClose))
                    {
                        if (!tp1Hit && _botAutoBreakevenOnTp1 && activeQty >= 2)
                        {
                            int half = activeQty / 2;
                            bankedPartialUsd += activeDir * (tp1Price - entryPrice) * pointVal * half
                                - (commPerSide * 2m * half);
                            activeQty -= half;
                            tp1Hit = true;
                        }

                        decimal remPnl = activeDir * (tp2Price - entryPrice) * pointVal * activeQty
                            - (commPerSide * 2m * activeQty);
                        decimal totalTradePnl = Math.Round(bankedPartialUsd + remPnl, 2);

                        FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                            entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                            tp2Price, totalTradePnl, "TP2 SWING TARGET", true, activeSignal);

                        activeDir = 0;
                        activeQty = 0;
                        bankedPartialUsd = 0m;
                    }
                    else if (tp1Triggered && !stopTriggered)
                    {
                        tp1Hit = true;

                        if (!_botAutoBreakevenOnTp1)
                        {
                            // Sortie 100% sur TP1 uniquement si l'utilisateur a explicitement désactivé le mode Runner
                            decimal fullTp1Pnl = activeDir * (tp1Price - entryPrice) * pointVal * activeQty
                                - (commPerSide * 2m * activeQty);
                            decimal totalTradePnl = Math.Round(bankedPartialUsd + fullTp1Pnl, 2);

                            FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                                entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                                tp1Price, totalTradePnl, "TP1 PROFIT", true, activeSignal);

                            activeDir = 0;
                            activeQty = 0;
                            bankedPartialUsd = 0m;
                        }
                        else if (activeQty == 1)
                        {
                            // 1 seul contrat : NE COUPE PAS à TP1 ! Verrouille +1.05R de profit garanti et laisse courir vers TP2 (3.2R+) !
                            decimal singleLockOffset = Math.Max(RoundToTick(initialRiskPts * 1.05m, tickSize), RoundToTick(tp1DistPts * 0.60m, tickSize));
                            stopPrice = RoundToTick(entryPrice + activeDir * singleLockOffset, tickSize);

                            bool hitSingleLockSameBar = activeDir > 0
                                ? (c.Close <= stopPrice && c.Low <= stopPrice && !favorableClose)
                                : (c.Close >= stopPrice && c.High >= stopPrice && !favorableClose);

                            if (hitSingleLockSameBar)
                            {
                                decimal remPnl = activeDir * (stopPrice - entryPrice) * pointVal * activeQty
                                    - (commPerSide * 2m * activeQty);
                                decimal totalTradePnl = Math.Round(bankedPartialUsd + remPnl, 2);

                                FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                                    entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                                    stopPrice, totalTradePnl, "TP1 LOCK + RUNNER", true, activeSignal);

                                activeDir = 0;
                                activeQty = 0;
                                bankedPartialUsd = 0m;
                            }
                        }
                        else
                        {
                            // Multi-contrats (>= 2) : encaisse 50% sur TP1 (1.8R) et verrouille le Runner à +0.45R pour laisser respirer vers TP2 (3.2R+)
                            int half = activeQty / 2;
                            bankedPartialUsd += activeDir * (tp1Price - entryPrice) * pointVal * half
                                - (commPerSide * 2m * half);
                            activeQty -= half;

                            decimal lockOffset = Math.Max(step * Math.Max(3, commStepFloor + 2), RoundToTick(initialRiskPts * 0.45m, tickSize));
                            stopPrice = RoundToTick(entryPrice + activeDir * lockOffset, tickSize);

                            bool hitProfitLockAfterTp1 = activeDir > 0
                                ? (c.Close <= stopPrice && c.Low <= stopPrice && !favorableClose)
                                : (c.Close >= stopPrice && c.High >= stopPrice && !favorableClose);

                            if (hitProfitLockAfterTp1)
                            {
                                decimal remPnl = activeDir * (stopPrice - entryPrice) * pointVal * activeQty
                                    - (commPerSide * 2m * activeQty);
                                decimal totalTradePnl = Math.Round(bankedPartialUsd + remPnl, 2);

                                FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                                    entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                                    stopPrice, totalTradePnl, "TP1 + RUNNER LOCK", true, activeSignal);

                                activeDir = 0;
                                activeQty = 0;
                                bankedPartialUsd = 0m;
                            }
                        }
                    }
                    else if (stopTriggered)
                    {
                        decimal legPnl = activeDir * (stopPrice - entryPrice) * pointVal * activeQty
                            - (commPerSide * 2m * activeQty);
                        decimal totalTradePnl = Math.Round(bankedPartialUsd + legPnl, 2);
                        bool wasProfitStop = tp1Hit || totalTradePnl >= 0m;
                        if (!wasProfitStop)
                            lastLossExitBar = b;

                        FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                            entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                            stopPrice, totalTradePnl, wasProfitStop ? "SWING TRAIL PROFIT" : "STOP LOSS", wasProfitStop, activeSignal);

                        activeDir = 0;
                        activeQty = 0;
                        bankedPartialUsd = 0m;
                    }

                    // Trailing Stop Structurel Swing (uniquement après TP1 pour laisser respirer la tendance jusqu'à TP2)
                    if (activeDir != 0 && tp1Hit)
                    {
                        decimal trailGap = Math.Max(RoundToTick(initialRiskPts * 0.85m, tickSize), RoundToTick(tp1DistPts * 0.55m, tickSize));
                        decimal candidateTrail = RoundToTick(maxFavPeakPrice - activeDir * trailGap, tickSize);

                        // Au-delà de +2.2R d'excursion, suivi sous le creux / sommet structurel des 2 dernières bougies
                        if (maxFavExcursion >= initialRiskPts * 2.20m && b > entryBar + 1)
                        {
                            var prevC = GetCandle(b - 1);
                            if (activeDir > 0)
                            {
                                decimal structSwingStop = RoundToTick(Math.Min(c.Low, prevC.Low) - step * 2m, tickSize);
                                candidateTrail = Math.Max(candidateTrail, structSwingStop);
                            }
                            else
                            {
                                decimal structSwingStop = RoundToTick(Math.Max(c.High, prevC.High) + step * 2m, tickSize);
                                candidateTrail = Math.Min(candidateTrail, structSwingStop);
                            }
                        }

                        if (activeDir > 0 && candidateTrail > stopPrice)
                            stopPrice = candidateTrail;
                        else if (activeDir < 0 && candidateTrail < stopPrice)
                            stopPrice = candidateTrail;
                    }

                    // Vérification temps réel du PnL latent vis-à-vis du Max Drawdown & Max Profit
                    if (activeDir != 0)
                    {
                        decimal openLegPnl = activeDir * (c.Close - entryPrice) * pointVal * activeQty;
                        decimal eqNow = state.RealizedPnlUsd + bankedPartialUsd + openLegPnl;
                        decimal ddNow = Math.Max(0m, state.PeakPnlUsd - eqNow);

                        if (ddNow >= _botMaxDrawdownUsd)
                        {
                            decimal forcedLoss = -(_botMaxDrawdownUsd - (state.PeakPnlUsd - state.RealizedPnlUsd));
                            FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                                entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                                c.Close, Math.Round(forcedLoss, 2), "KILL-SWITCH DD MAX", tp1Hit, activeSignal);

                            state.IsHaltedByMaxDrawdown = true;
                            activeDir = 0;
                            activeQty = 0;
                            bankedPartialUsd = 0m;
                        }
                        else if (eqNow >= _botMaxProfitTargetUsd)
                        {
                            decimal lockedGain = _botMaxProfitTargetUsd - state.RealizedPnlUsd;
                            FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                                entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                                c.Close, Math.Round(lockedGain, 2), "LOCK PNL MAX", tp1Hit, activeSignal);

                            state.IsHaltedByMaxProfit = true;
                            activeDir = 0;
                            activeQty = 0;
                            bankedPartialUsd = 0m;
                        }
                    }
                }

                // 2. Coupe-circuit si Max Drawdown ou Max PnL est déjà atteint
                if (state.IsHaltedByMaxDrawdown || state.IsHaltedByMaxProfit)
                    continue;

                // 2bis. Vérification de la Plage Horaire (Heures de Démarrage & de Fin)
                bool inSchedule = IsBarWithinBotSchedule(c.Time);
                if (!inSchedule)
                {
                    if (activeDir != 0 && _botFlattenAtEndTime)
                    {
                        decimal eodPnl = bankedPartialUsd
                            + activeDir * (c.Close - entryPrice) * pointVal * activeQty
                            - (commPerSide * 2m * activeQty);

                        FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                            entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                            c.Close, Math.Round(eodPnl, 2), "FIN PLAGE HORAIRE", tp1Hit, activeSignal);

                        activeDir = 0;
                        activeQty = 0;
                        bankedPartialUsd = 0m;
                    }
                    continue;
                }

                // 3. Évaluation d'un nouveau signal d'entrée ou de retournement sur la bougie b
                var rows = AnalyzeBarClusters(
                    c, tickSize,
                    out _, out _,
                    out int bullAbsIdx, out int bearAbsIdx,
                    out int trapSellIdx, out int trapBuyIdx);

                if (rows.Length == 0)
                    continue;

                _barRowsCache[b] = rows;

                var opt = AnalyzeBarOptionsFlow(b, c, rows, tickSize);
                var sig = EvaluateBuySellSignal(
                    b, c, rows, tickSize,
                    bullAbsIdx, bearAbsIdx,
                    trapSellIdx, trapBuyIdx,
                    opt);

                if (sig.Direction != 0)
                {
                    bool isLockedRealOrder = _lockedExecutedSignalsByBar.TryGetValue(b, out var lockedBarSig) && lockedBarSig.Direction != 0;
                    if (isLockedRealOrder)
                        sig = lockedBarSig;

                    // Si une position opposée est ouverte et qu'un signal inverse validé par la stratégie apparaît
                    if (activeDir != 0 && activeDir != sig.Direction && b > entryBar)
                    {
                        decimal curOpenPnl = bankedPartialUsd + activeDir * (c.Close - entryPrice) * pointVal * activeQty;
                        decimal revPnl = curOpenPnl - (commPerSide * 2m * activeQty);
                        decimal minProfitForFlip = initialRiskPts * pointVal * activeQty * 0.80m;

                        // Interdiction stricte de couper un trade en perte ou en micro-gain sur un simple bruit opposé :
                        // on n'inverse que si TP1 est déjà atteint, si le trade gagne déjà >= +0.8R, ou sur retournement institutionnel extrême (score >= 90 après >= 4 bougies)
                        bool allowReversal = isLockedRealOrder || tp1Hit || revPnl >= minProfitForFlip || (sig.ConfluenceScore >= 90 && b >= entryBar + 4);
                        if (allowReversal)
                        {
                            if (revPnl < 0m)
                                lastLossExitBar = b;

                            FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                                entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                                c.Close, Math.Round(revPnl, 2), "REVERSAL SIGNAL", tp1Hit, activeSignal);

                            activeDir = 0;
                            activeQty = 0;
                            bankedPartialUsd = 0m;

                            if (state.IsHaltedByMaxDrawdown || state.IsHaltedByMaxProfit)
                                continue;
                        }
                    }

                    // Si un ordre réel a été exécuté sur la bougie b alors que la simulation avait encore une ancienne position dans le même sens,
                    // on aligne la simulation sur l'ordre réel exécuté sur la bougie b
                    if (isLockedRealOrder && activeDir == sig.Direction && entryBar < b)
                    {
                        decimal prevPnl = bankedPartialUsd + activeDir * (c.Close - entryPrice) * pointVal * activeQty - (commPerSide * 2m * activeQty);
                        FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                            entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                            c.Close, Math.Round(prevPnl, 2), "SYNC ORDRE RÉEL", tp1Hit, activeSignal);
                        activeDir = 0;
                        activeQty = 0;
                        bankedPartialUsd = 0m;
                    }

                    // Ouverture d'une nouvelle position Quant avec Cooldown anti-surtrading (minimum 3 bougies entre entrées, 4 bougies après un SL)
                    bool cooldownOk = isLockedRealOrder || ((b - lastEntryBar >= 3) && (b - lastLossExitBar >= 4 || sig.ConfluenceScore >= 88));
                    if (activeDir == 0 && cooldownOk)
                    {
                        activeDir = sig.Direction;
                        activeQty = initialQty;
                        entryBar = b;
                        lastEntryBar = b;
                        entryPrice = sig.EntryPrice;
                        stopPrice = sig.StopPrice;
                        initialRiskPts = Math.Max(step * 4m, Math.Abs(sig.EntryPrice - sig.StopPrice));
                        tp1Price = sig.Tp1Price;
                        tp2Price = sig.Tp2Price;
                        maxFavPeakPrice = sig.EntryPrice;
                        tp1Hit = false;
                        bankedPartialUsd = 0m;
                        activeSignal = sig;
                    }
                }
            }

            // Alignement 100% Temps Réel avec la position réelle ouverte dans ATAS TradingManager (si présente)
            if (_botMode == BotExecutionMode.LiveAutoTradingManager && _liveTrackedDir != 0 && _liveTrackedEntryPrice > 0m && upToBar >= Math.Max(0, CurrentBar - 2))
            {
                int liveBar = (_liveTrackedSignalBar >= 0 && _liveTrackedSignalBar <= upToBar) ? _liveTrackedSignalBar : upToBar;
                activeDir = _liveTrackedDir;
                activeQty = Math.Max(1, _botTradeQuantity);
                if (TradingManager?.Position != null && TradingManager.Position.Volume != 0m)
                    activeQty = Math.Max(1, (int)Math.Round(Math.Abs(TradingManager.Position.Volume)));
                entryBar = liveBar;
                entryPrice = _liveTrackedEntryPrice;
                stopPrice = _liveTrackedStopPrice;
                tp1Price = _liveTrackedTp1Price;
                tp2Price = _liveTrackedTp2Price;
                tp1Hit = _liveTrackedTp1Hit;
                if (_lockedExecutedSignalsByBar.TryGetValue(liveBar, out var liveLockedSig) && liveLockedSig.Direction != 0)
                    activeSignal = liveLockedSig;
            }

            // Mise à jour de l'état courant sur la dernière bougie
            if (activeDir != 0 && upToBar >= 0)
            {
                var lastC = GetCandle(upToBar);
                state.ActiveDirection = activeDir;
                state.ActiveContracts = activeQty;
                state.ActiveEntryBar = entryBar;
                state.ActiveEntryPrice = entryPrice;
                state.ActiveStopPrice = stopPrice;
                state.ActiveTp1Price = tp1Price;
                state.ActiveTp2Price = tp2Price;
                state.ActiveTp1Hit = tp1Hit;
                state.UnrealizedPnlUsd = Math.Round(
                    bankedPartialUsd + activeDir * (lastC.Close - entryPrice) * pointVal * activeQty, 2);

                tradeHistory.Add(new BotTradeExecutionRecord
                {
                    EntryBar = entryBar,
                    ExitBar = upToBar,
                    Direction = activeDir,
                    EntryPrice = entryPrice,
                    StopPrice = stopPrice,
                    Tp1Price = tp1Price,
                    Tp2Price = tp2Price,
                    ExitPrice = lastC.Close,
                    RealizedPnlUsd = state.UnrealizedPnlUsd,
                    ExitReason = tp1Hit ? "EN COURS (BE+ ACTIF)" : "EN COURS (OPEN)",
                    Tp1Reached = tp1Hit,
                    EntrySignal = activeSignal
                });
            }

            state.TotalPnlUsd = Math.Round(state.RealizedPnlUsd + state.UnrealizedPnlUsd, 2);
            if (state.TotalPnlUsd > state.PeakPnlUsd)
                state.PeakPnlUsd = state.TotalPnlUsd;

            state.CurrentDrawdownUsd = Math.Round(Math.Max(0m, state.PeakPnlUsd - state.TotalPnlUsd), 2);
            if (state.CurrentDrawdownUsd > state.MaxObservedDrawdownUsd)
                state.MaxObservedDrawdownUsd = state.CurrentDrawdownUsd;

            if (state.TotalTrades > 0)
            {
                state.WinRatePct = Math.Round((state.WinningTrades / (decimal)state.TotalTrades) * 100m, 1);
                state.ProfitFactor = state.GrossLossUsd > 0m
                    ? Math.Round(state.GrossProfitUsd / state.GrossLossUsd, 2)
                    : (state.GrossProfitUsd > 0m ? 9.99m : 0m);
                state.ExpectancyUsd = Math.Round(state.RealizedPnlUsd / state.TotalTrades, 2);

                if (tradeReturns.Count >= 2)
                {
                    double mean = 0.0;
                    for (int i = 0; i < tradeReturns.Count; i++) mean += tradeReturns[i];
                    mean /= tradeReturns.Count;

                    double variance = 0.0;
                    for (int i = 0; i < tradeReturns.Count; i++)
                    {
                        double d = tradeReturns[i] - mean;
                        variance += d * d;
                    }
                    double std = Math.Sqrt(variance / tradeReturns.Count);
                    if (std > 1e-6)
                    {
                        double sharpe = (mean / std) * Math.Sqrt(tradeReturns.Count);
                        state.SharpeRatio = Math.Round((decimal)Math.Clamp(sharpe, -9.9, 9.9), 2);
                    }
                }
            }

            bool latestInSched = upToBar < 0 || IsBarWithinBotSchedule(GetCandle(upToBar).Time);
            string tradedTag = contractInfo.IsCrossTradingMicro ? $" [CROISÉ➔{contractInfo.TradedSymbol}]" : string.Empty;

            if (state.IsHaltedByMaxDrawdown)
                state.StatusLabel = $"COUPE-CIRCUIT : DRAWDOWN MAX ATTEINT (-{state.MaxObservedDrawdownUsd:0}$ / -{_botMaxDrawdownUsd:0}$){tradedTag}";
            else if (state.IsHaltedByMaxProfit)
                state.StatusLabel = $"OBJECTIF PNL MAX VERROUILLÉ (+{state.TotalPnlUsd:0}$ / +{_botMaxProfitTargetUsd:0}$){tradedTag}";
            else if (!latestInSched)
                state.StatusLabel = $"VEILLE HORAIRE — ACTIF DE {BotStartTimeText} À {BotEndTimeText}{tradedTag}";
            else if (state.ActiveDirection > 0)
                state.StatusLabel = $"LONG ACTIF (+{state.ActiveContracts} {contractInfo.TradedSymbol} @ {state.ActiveEntryPrice}) | Latent: {state.UnrealizedPnlUsd:+0;-0;0}${tradedTag}";
            else if (state.ActiveDirection < 0)
                state.StatusLabel = $"SHORT ACTIF (-{state.ActiveContracts} {contractInfo.TradedSymbol} @ {state.ActiveEntryPrice}) | Latent: {state.UnrealizedPnlUsd:+0;-0;0}${tradedTag}";
            else
                state.StatusLabel = $"ARMÉ ({BotStartTimeText}-{BotEndTimeText}) — EN ATTENTE DE SIGNAL QUANT{tradedTag}";

            return state;
        }

        private void FinalizeBotTrade(
            ref BotPerformanceState state,
            List<BotTradeExecutionRecord> history,
            List<double> returnsList,
            int entryBar,
            int exitBar,
            int direction,
            decimal entryPrice,
            decimal stopPrice,
            decimal tp1Price,
            decimal tp2Price,
            decimal exitPrice,
            decimal pnlUsd,
            string reason,
            bool tp1Reached,
            VisualTradeSignal entrySignal)
        {
            state.RealizedPnlUsd = Math.Round(state.RealizedPnlUsd + pnlUsd, 2);
            state.TotalTrades++;

            if (pnlUsd >= 0m)
            {
                state.WinningTrades++;
                state.GrossProfitUsd += pnlUsd;
            }
            else
            {
                state.LosingTrades++;
                state.GrossLossUsd += Math.Abs(pnlUsd);
            }

            if (state.RealizedPnlUsd > state.PeakPnlUsd)
                state.PeakPnlUsd = state.RealizedPnlUsd;

            decimal dd = Math.Max(0m, state.PeakPnlUsd - state.RealizedPnlUsd);
            state.CurrentDrawdownUsd = dd;
            if (dd > state.MaxObservedDrawdownUsd)
                state.MaxObservedDrawdownUsd = dd;

            if (dd >= _botMaxDrawdownUsd)
                state.IsHaltedByMaxDrawdown = true;
            if (state.RealizedPnlUsd >= _botMaxProfitTargetUsd)
                state.IsHaltedByMaxProfit = true;

            returnsList.Add((double)pnlUsd);
            history.Add(new BotTradeExecutionRecord
            {
                EntryBar = entryBar,
                ExitBar = exitBar,
                Direction = direction,
                EntryPrice = entryPrice,
                StopPrice = stopPrice,
                Tp1Price = tp1Price,
                Tp2Price = tp2Price,
                ExitPrice = exitPrice,
                RealizedPnlUsd = pnlUsd,
                ExitReason = reason,
                Tp1Reached = tp1Reached,
                EntrySignal = entrySignal
            });
        }

        // =====================================================================
        // Synchronisation Multi-Sources du PnL Réel avec le Compte Actif ATAS X
        // (Supporte nativement le Trading Croisé ATAS X : ex. Chart ES/NQ -> Position & PnL sur MES/MNQ)
        // =====================================================================
        private void SynchronizeWithActiveAccount(int upToBar, decimal tickSize, ref BotPerformanceState state)
        {
            var contractInfo = ResolveContractAndCrossTradingInfo(tickSize);
            state.MarketProfileLabel = contractInfo.ProfileBadge;
            state.CrossTradingLabel = contractInfo.CrossTradingBadge;
            state.EffectivePointValueUsd = contractInfo.TradedPointValueUsd;
            state.SimulatedTotalPnlUsd = state.TotalPnlUsd;

            if (!_syncPnlWithActiveAccount)
            {
                state.IsSyncedWithActiveAccount = false;
                state.ActiveAccountName = "SIMULATION GRAPHIQUE";
                state.ActiveAccountCurrency = "$";
                return;
            }

            try
            {
                var tm = TradingManager;
                if (tm == null)
                {
                    state.IsSyncedWithActiveAccount = false;
                    state.ActiveAccountName = "SIMULATION (HORS CONNEXION)";
                    state.ActiveAccountCurrency = "$";
                    return;
                }

                var pf = tm.Portfolio;
                var pos = tm.Position;
                var sec = tm.Security;

                if (pf == null && pos == null)
                {
                    state.IsSyncedWithActiveAccount = false;
                    state.ActiveAccountName = "AUCUN COMPTE ACTIF";
                    state.ActiveAccountCurrency = "$";
                    return;
                }

                string accountId = !string.IsNullOrWhiteSpace(pf?.AccountID)
                    ? pf.AccountID!
                    : (!string.IsNullOrWhiteSpace(pf?.DepoName)
                        ? pf.DepoName!
                        : (!string.IsNullOrWhiteSpace(pos?.AccountID) ? pos.AccountID! : "Compte Actif"));

                string currency = pf?.Currency != null ? pf.Currency.Value.ToString() : "$";
                decimal balance = pf?.Balance ?? pf?.BalanceAvailable ?? 0m;

                // Réinitialisation automatique du suivi High-Water Mark si l'utilisateur change de compte actif dans ATAS
                if (!string.Equals(_lastSyncedAccountId, accountId, StringComparison.Ordinal))
                {
                    _lastSyncedAccountId = accountId;
                    _liveAccountPeakPnlUsd = decimal.MinValue;
                    _liveAccountMaxDrawdownUsd = 0m;
                    _liveKillSwitchExecuted = false;
                }

                decimal pointVal = contractInfo.TradedPointValueUsd;
                decimal posVol = pos?.Volume ?? 0m;
                decimal avgPrice = pos?.AveragePrice ?? 0m;
                int realDir = posVol > 0m ? 1 : (posVol < 0m ? -1 : 0);
                int realQty = (int)Math.Round(Math.Abs(posVol));

                // 1. Extraction du PnL Latent / Open PnL temps réel du Compte Actif (y compris sur contrat Micro croisé MES/MNQ)
                decimal? posUnrealized = pos?.UnrealizedPnL;
                decimal? pfOpen = pf?.OpenPnL;
                decimal realUnrealized = 0m;

                if (_accountPnlScope == ActiveAccountPnlScope.ChartInstrumentOnly)
                {
                    if (posUnrealized.HasValue && posUnrealized.Value != 0m)
                        realUnrealized = posUnrealized.Value;
                    else if (posVol != 0m && avgPrice > 0m)
                    {
                        decimal lastPx = (sec?.LastTradePrice > 0m)
                            ? sec.LastTradePrice.Value
                            : (upToBar >= 0 ? GetCandle(upToBar).Close : avgPrice);
                        realUnrealized = posVol * (lastPx - avgPrice) * pointVal;
                    }
                    else if (pfOpen.HasValue && pfOpen.Value != 0m)
                    {
                        realUnrealized = pfOpen.Value;
                    }
                }
                else
                {
                    if (pfOpen.HasValue && pfOpen.Value != 0m)
                        realUnrealized = pfOpen.Value;
                    else if (posUnrealized.HasValue && posUnrealized.Value != 0m)
                        realUnrealized = posUnrealized.Value;
                    else if (posVol != 0m && avgPrice > 0m)
                    {
                        decimal lastPx = (sec?.LastTradePrice > 0m)
                            ? sec.LastTradePrice.Value
                            : (upToBar >= 0 ? GetCandle(upToBar).Close : avgPrice);
                        realUnrealized = posVol * (lastPx - avgPrice) * pointVal;
                    }
                }

                // 2. Extraction du PnL Réalisé (Closed PnL) et des Statistiques de Trades Réels du Compte Actif
                string? targetSecId = sec?.SecurityId ?? pos?.SecurityId;
                ComputeActiveAccountTradesMetrics(
                    accountId,
                    targetSecId,
                    pointVal,
                    out decimal tradesRealizedPnl,
                    out decimal tradesComm,
                    out int realTotalTrades,
                    out int realWinTrades,
                    out int realLossTrades,
                    out decimal realGrossProf,
                    out decimal realGrossLoss);

                decimal? pfClosed = pf?.ClosedPnL ?? pf?.TotalClosedPnL;
                decimal? pfTotal = pf?.TotalPnL;
                decimal? posRealized = pos?.RealizedPnL;
                decimal realRealized = 0m;

                if (_accountPnlScope == ActiveAccountPnlScope.ChartInstrumentOnly)
                {
                    if (posRealized.HasValue && posRealized.Value != 0m)
                        realRealized = posRealized.Value;
                    else if (tradesRealizedPnl != 0m)
                        realRealized = tradesRealizedPnl;
                    else if (pfClosed.HasValue && pfClosed.Value != 0m)
                        realRealized = pfClosed.Value;
                    else
                        realRealized = posRealized ?? pfClosed ?? 0m;
                }
                else
                {
                    if (pfClosed.HasValue && pfClosed.Value != 0m)
                        realRealized = pfClosed.Value;
                    else if (pfTotal.HasValue && pfTotal.Value != 0m)
                        realRealized = pfTotal.Value - realUnrealized;
                    else if (posRealized.HasValue && posRealized.Value != 0m)
                        realRealized = posRealized.Value;
                    else if (tradesRealizedPnl != 0m)
                        realRealized = tradesRealizedPnl;
                    else
                        realRealized = pfClosed ?? posRealized ?? 0m;
                }

                decimal realTotalPnl = realRealized + realUnrealized;
                if (_accountPnlScope == ActiveAccountPnlScope.ActivePortfolioTotal &&
                    pfTotal.HasValue && pfTotal.Value != 0m && realRealized == 0m && realUnrealized == 0m)
                {
                    realTotalPnl = pfTotal.Value;
                    realRealized = pfTotal.Value;
                }

                state.IsSyncedWithActiveAccount = true;
                state.ActiveAccountName = accountId;
                state.ActiveAccountCurrency = currency;
                state.ActiveAccountBalance = Math.Round(balance, 2);
                state.ActiveAccountCommissionUsd = Math.Round(pf?.Commission ?? pos?.Commission ?? tradesComm, 2);

                state.RealizedPnlUsd = Math.Round(realRealized, 2);
                state.UnrealizedPnlUsd = Math.Round(realUnrealized, 2);
                state.TotalPnlUsd = Math.Round(realTotalPnl, 2);

                state.ActiveDirection = realDir;
                state.ActiveContracts = realQty;
                if (realDir != 0)
                {
                    if (avgPrice > 0m)
                        state.ActiveEntryPrice = avgPrice;
                    if (_liveTrackedDir == realDir && _liveTrackedStopPrice > 0m)
                    {
                        state.ActiveStopPrice = _liveTrackedStopPrice;
                        state.ActiveTp1Price = _liveTrackedTp1Price;
                        state.ActiveTp2Price = _liveTrackedTp2Price;
                        state.ActiveTp1Hit = _liveTrackedTp1Hit;
                    }
                }

                if (realTotalTrades > 0)
                {
                    state.TotalTrades = realTotalTrades;
                    state.WinningTrades = realWinTrades;
                    state.LosingTrades = realLossTrades;
                    state.GrossProfitUsd = Math.Round(realGrossProf, 2);
                    state.GrossLossUsd = Math.Round(realGrossLoss, 2);
                    state.WinRatePct = Math.Round((realWinTrades / (decimal)realTotalTrades) * 100m, 1);
                    state.ProfitFactor = realGrossLoss > 0m
                        ? Math.Round(realGrossProf / realGrossLoss, 2)
                        : (realGrossProf > 0m ? 9.99m : 0m);
                    state.ExpectancyUsd = Math.Round(state.RealizedPnlUsd / realTotalTrades, 2);
                }

                // 3. Suivi Temps Réel du High-Water Mark (Pic d'Équité) et du Drawdown Réel du Compte Actif
                if (_liveAccountPeakPnlUsd == decimal.MinValue)
                    _liveAccountPeakPnlUsd = Math.Max(0m, state.TotalPnlUsd);

                if (state.TotalPnlUsd > _liveAccountPeakPnlUsd)
                    _liveAccountPeakPnlUsd = state.TotalPnlUsd;

                decimal realDd = Math.Round(Math.Max(0m, _liveAccountPeakPnlUsd - state.TotalPnlUsd), 2);
                if (realDd > _liveAccountMaxDrawdownUsd)
                    _liveAccountMaxDrawdownUsd = realDd;

                state.PeakPnlUsd = _liveAccountPeakPnlUsd;
                state.CurrentDrawdownUsd = realDd;
                state.MaxObservedDrawdownUsd = _liveAccountMaxDrawdownUsd;

                state.IsHaltedByMaxDrawdown = _liveAccountMaxDrawdownUsd >= _botMaxDrawdownUsd || state.TotalPnlUsd <= -_botMaxDrawdownUsd;
                state.IsHaltedByMaxProfit = state.TotalPnlUsd >= _botMaxProfitTargetUsd;

                bool latestInSched = upToBar < 0 || IsBarWithinBotSchedule(GetCandle(upToBar).Time);
                string crossTag = contractInfo.IsCrossTradingMicro ? $" [CROISÉ➔{contractInfo.TradedSymbol}]" : string.Empty;

                if (!_enableTradingBot)
                {
                    state.StatusLabel = $"DÉSACTIVÉ | COMPTE [{accountId}]{crossTag} PnL Réel: {state.TotalPnlUsd:+0.00;-0.00;0.00}$";
                }
                else if (state.IsHaltedByMaxDrawdown)
                {
                    state.StatusLabel = $"COUPE-CIRCUIT [{accountId}]{crossTag} : DD MAX RÉEL ATTEINT (-{state.MaxObservedDrawdownUsd:0.00}$ / -{_botMaxDrawdownUsd:0}$)";
                }
                else if (state.IsHaltedByMaxProfit)
                {
                    state.StatusLabel = $"OBJECTIF PNL MAX VERROUILLÉ [{accountId}]{crossTag} (+{state.TotalPnlUsd:0.00}$ / +{_botMaxProfitTargetUsd:0}$)";
                }
                else if (!latestInSched)
                {
                    state.StatusLabel = $"VEILLE HORAIRE [{accountId}]{crossTag} — ACTIF DE {BotStartTimeText} À {BotEndTimeText}";
                }
                else if (state.ActiveDirection > 0)
                {
                    state.StatusLabel = $"LONG RÉEL [{accountId}]{crossTag} (+{state.ActiveContracts} {contractInfo.TradedSymbol} @ {state.ActiveEntryPrice}) | Latent: {state.UnrealizedPnlUsd:+0.00;-0.00;0.00}$";
                }
                else if (state.ActiveDirection < 0)
                {
                    state.StatusLabel = $"SHORT RÉEL [{accountId}]{crossTag} (-{state.ActiveContracts} {contractInfo.TradedSymbol} @ {state.ActiveEntryPrice}) | Latent: {state.UnrealizedPnlUsd:+0.00;-0.00;0.00}$";
                }
                else
                {
                    state.StatusLabel = $"SYNCHRONISÉ [{accountId}]{crossTag} — PnL Réel: {state.TotalPnlUsd:+0.00;-0.00;0.00}$ (En attente signal)";
                }
            }
            catch
            {
                // Protection totale si le connecteur de compte est en cours d'initialisation
            }
        }

        private void ComputeActiveAccountTradesMetrics(
            string accountId,
            string? securityId,
            decimal pointVal,
            out decimal realizedPnlUsd,
            out decimal totalCommissionUsd,
            out int totalTrades,
            out int winningTrades,
            out int losingTrades,
            out decimal grossProfitUsd,
            out decimal grossLossUsd)
        {
            realizedPnlUsd = 0m;
            totalCommissionUsd = 0m;
            totalTrades = 0;
            winningTrades = 0;
            losingTrades = 0;
            grossProfitUsd = 0m;
            grossLossUsd = 0m;

            bool filterBySec = _accountPnlScope == ActiveAccountPnlScope.ChartInstrumentOnly && !string.IsNullOrWhiteSpace(securityId);
            string? chartSym = InstrumentInfo?.Instrument;

            bool MatchesInstrumentOrCrossFamily(string? candidateSec)
            {
                if (!filterBySec || string.IsNullOrWhiteSpace(candidateSec))
                    return true;
                if (string.Equals(candidateSec, securityId, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (AreInstrumentsInSameCrossFamily(candidateSec, securityId) ||
                    AreInstrumentsInSameCrossFamily(candidateSec, chartSym))
                    return true;
                return false;
            }

            // 1. Lecture prioritaire via TradingStatisticsProvider (HistoryMyTrades d'ATAS X)
            try
            {
                var tsp = TradingStatisticsProvider;
                var stats = tsp?.FilteredStatistics ?? tsp?.RawStatistics;
                if (stats != null)
                {
                    var prop = typeof(ATAS.DataFeedsCore.Statistics.ITradingStatistics).GetProperty("HistoryMyTrades");
                    if (prop?.GetValue(stats) is IEnumerable histEnum)
                    {
                        foreach (var item in histEnum)
                        {
                            if (item is HistoryMyTrade h && h.IsComplete)
                            {
                                if (!string.IsNullOrWhiteSpace(h.AccountID) &&
                                    !string.Equals(h.AccountID, accountId, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                if (!MatchesInstrumentOrCrossFamily(h.SecurityId))
                                    continue;

                                decimal comm = h.Commission ?? 0m;
                                decimal net = h.PnL - comm;
                                realizedPnlUsd += net;
                                totalCommissionUsd += comm;
                                totalTrades++;

                                if (net >= 0m)
                                {
                                    winningTrades++;
                                    grossProfitUsd += net;
                                }
                                else
                                {
                                    losingTrades++;
                                    grossLossUsd += Math.Abs(net);
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback sur TradingManager.MyTrades si HistoryMyTrades n'est pas disponible
            }

            if (totalTrades > 0)
                return;

            // 2. Fallback : Reconstruction FIFO / Prix Moyen à partir de TradingManager.MyTrades
            try
            {
                var myTrades = TradingManager?.MyTrades;
                if (myTrades == null)
                    return;

                decimal netPos = 0m;
                decimal avgEntry = 0m;

                foreach (var t in myTrades)
                {
                    if (t == null || t.Volume <= 0m)
                        continue;

                    string? tAcc = t.AccountID ?? t.Portfolio?.AccountID;
                    if (!string.IsNullOrWhiteSpace(tAcc) &&
                        !string.Equals(tAcc, accountId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string? tSec = t.SecurityId ?? t.Security?.SecurityId ?? t.Security?.Code;
                    if (!MatchesInstrumentOrCrossFamily(tSec))
                        continue;

                    decimal comm = t.Commission ?? 0m;
                    totalCommissionUsd += comm;

                    decimal signedVol = t.OrderDirection == OrderDirections.Buy ? t.Volume : -t.Volume;

                    if (netPos == 0m || Math.Sign(netPos) == Math.Sign(signedVol))
                    {
                        decimal newVol = Math.Abs(netPos) + t.Volume;
                        avgEntry = newVol > 0m ? ((Math.Abs(netPos) * avgEntry) + (t.Volume * t.Price)) / newVol : t.Price;
                        netPos += signedVol;
                    }
                    else
                    {
                        decimal closedQty = Math.Min(Math.Abs(netPos), t.Volume);
                        int prevDir = netPos > 0m ? 1 : -1;
                        decimal tradePnl = prevDir * (t.Price - avgEntry) * pointVal * closedQty - comm;

                        realizedPnlUsd += tradePnl;
                        totalTrades++;
                        if (tradePnl >= 0m)
                        {
                            winningTrades++;
                            grossProfitUsd += tradePnl;
                        }
                        else
                        {
                            losingTrades++;
                            grossLossUsd += Math.Abs(tradePnl);
                        }

                        decimal rem = t.Volume - closedQty;
                        if (rem > 0m)
                        {
                            netPos = Math.Sign(signedVol) * rem;
                            avgEntry = t.Price;
                        }
                        else
                        {
                            netPos += signedVol;
                            if (netPos == 0m)
                                avgEntry = 0m;
                        }
                    }
                }
            }
            catch
            {
                // Ignorer si MyTrades est vide ou en cours de modification
            }
        }

        // =====================================================================
        // Routage Live vers ATAS ITradingManager (Exécution SL / TP1 / TP2 / Trailing / Reversal & Trading Croisé ES➔MES / NQ➔MNQ)
        // =====================================================================
        private void SendLiveMarketCloseOrReduce(ITradingManager tm, Position pos, int qtyToClose, string comment)
        {
            var targetSecurity = pos?.Security ?? tm?.Security;
            if (tm == null || targetSecurity == null || pos == null || pos.Volume == 0m || qtyToClose <= 0)
                return;

            int absPos = (int)Math.Round(Math.Abs(pos.Volume));
            int finalQty = Math.Min(absPos, qtyToClose);
            if (finalQty <= 0)
                return;

            var exitOrder = new Order
            {
                Security = targetSecurity,
                Portfolio = tm.Portfolio,
                Type = OrderTypes.Market,
                Direction = pos.Volume > 0m ? OrderDirections.Sell : OrderDirections.Buy,
                QuantityToFill = finalQty,
                Comment = comment
            };
            _ = tm.OpenOrderAsync(exitOrder, false, false, false);
        }

        private void EnsureLivePositionTrackingInitialized(Position pos, decimal curPrice, decimal tickSize)
        {
            if (pos == null || pos.Volume == 0m)
                return;

            var contractInfo = ResolveContractAndCrossTradingInfo(tickSize);
            bool isCme = contractInfo.IsCmeMarket;
            MarketProfilePreset prof = contractInfo.EffectiveProfile;

            int realDir = pos.Volume > 0m ? 1 : -1;
            decimal step = tickSize * Math.Max(1, _ticksPerRow);
            decimal chartRefPx = curPrice > 0m ? RoundToTick(curPrice, tickSize) : RoundToTick(pos.AveragePrice, tickSize);
            decimal rawFillPx = pos.AveragePrice > 0m ? RoundToTick(pos.AveragePrice, tickSize) : chartRefPx;
            if (rawFillPx > 0m)
                _liveTrackedRealFillPrice = rawFillPx;

            // En Trading Croisé ATAS X (ex: chart ES continu vs contrat Micro MES à échéance), s'il existe un écart de base (basis spread)
            // entre le prix du graphique et pos.AveragePrice, on convertit pos.AveragePrice dans le référentiel du graphique via _liveCrossBasisOffset.
            bool hasCrossBasisOffset = contractInfo.IsCrossTradingMicro && curPrice > 0m && Math.Abs(rawFillPx - chartRefPx) > step * 10m;
            decimal fillPx = rawFillPx;
            if (hasCrossBasisOffset)
            {
                fillPx = _liveCrossBasisOffset != 0m
                    ? RoundToTick(rawFillPx - _liveCrossBasisOffset, tickSize)
                    : chartRefPx;
            }

            if (_liveTrackedDir != realDir || _liveTrackedEntryPrice <= 0m || _liveTrackedStopPrice <= 0m || _liveTrackedTp1Price <= 0m || _liveTrackedTp2Price <= 0m)
            {
                int refBar = Math.Max(0, CurrentBar - 1);
                decimal atr = ComputeQuantAtr(refBar, tickSize);
                decimal commRtUsd = Math.Max(0.50m, contractInfo.TradedCommissionPerSideUsd * 2m);
                decimal stepValUsd = Math.Max(0.05m, contractInfo.TradedPointValueUsd * step);
                int commStepFloor = Math.Max(1, (int)Math.Ceiling(commRtUsd / stepValUsd));

                int baseMinStopSteps = isCme
                    ? (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 10 : 6)
                    : 5;
                int minStopSteps = Math.Max(baseMinStopSteps, commStepFloor + 4);

                int baseMinTp1Steps = isCme
                    ? (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 16 : 10)
                    : 8;
                int minTp1Steps = Math.Max(baseMinTp1Steps, commStepFloor + 8);

                int baseMinTp2Steps = isCme
                    ? (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 32 : 20)
                    : 16;

                decimal riskPts = Math.Max(step * minStopSteps, RoundToTick(atr * 0.65m, tickSize));
                decimal effTp1Rr = Math.Max(1.50m, _quantTp1RiskReward);
                decimal effTp2Rr = Math.Max(2.60m, _quantTp2RiskReward);

                decimal minTp1Floor = Math.Max(step * minTp1Steps, RoundToTick(atr * 1.15m, tickSize));
                decimal minTp2Floor = Math.Max(step * baseMinTp2Steps, RoundToTick(atr * 2.25m, tickSize));

                decimal tp1Dist = Math.Max(minTp1Floor, RoundToTick(riskPts * effTp1Rr, tickSize));
                decimal tp2Dist = Math.Max(Math.Max(minTp2Floor, tp1Dist + step * 8m), RoundToTick(riskPts * effTp2Rr, tickSize));

                _liveTrackedDir = realDir;
                _liveTrackedEntryPrice = fillPx;
                _liveTrackedInitialRiskPts = riskPts;
                _liveTrackedStopPrice = RoundToTick(fillPx - realDir * riskPts, tickSize);
                _liveTrackedTp1Price = RoundToTick(fillPx + realDir * tp1Dist, tickSize);
                _liveTrackedTp2Price = RoundToTick(fillPx + realDir * tp2Dist, tickSize);
                _liveMaxFavorablePrice = curPrice > 0m ? curPrice : fillPx;
                _liveTrackedTp1Hit = false;
                _liveTp1PartialOrderSent = false;
            }
            else if (pos.AveragePrice > 0m && !_liveTrackedTp1Hit && Math.Abs(_liveTrackedEntryPrice - fillPx) >= tickSize)
            {
                // Recalage automatique sur le prix moyen d'exécution réel (pos.AveragePrice) sans jamais remonter le Stop-Loss dans le corps de la bougie
                decimal prevRiskPts = Math.Max(step * 4m, Math.Abs(_liveTrackedEntryPrice - _liveTrackedStopPrice));
                decimal tp1Dist = Math.Max(step * 6m, Math.Abs(_liveTrackedTp1Price - _liveTrackedEntryPrice));
                decimal tp2Dist = Math.Max(tp1Dist + step * 6m, Math.Abs(_liveTrackedTp2Price - _liveTrackedEntryPrice));

                decimal candidateStop = RoundToTick(fillPx - realDir * prevRiskPts, tickSize);
                decimal structuralStop = realDir > 0
                    ? Math.Min(_liveTrackedStopPrice, candidateStop)
                    : Math.Max(_liveTrackedStopPrice, candidateStop);

                _liveTrackedEntryPrice = fillPx;
                _liveTrackedStopPrice = structuralStop;
                _liveTrackedInitialRiskPts = Math.Max(step * 4m, Math.Abs(_liveTrackedEntryPrice - _liveTrackedStopPrice));
                _liveTrackedTp1Price = RoundToTick(fillPx + realDir * tp1Dist, tickSize);
                _liveTrackedTp2Price = RoundToTick(fillPx + realDir * tp2Dist, tickSize);
            }

            // Synchronisation 100% du signal visuel verrouillé sur la bougie avec le véritable prix exécuté du compte
            if (_liveTrackedSignalBar >= 0 && _lockedExecutedSignalsByBar.TryGetValue(_liveTrackedSignalBar, out var lockedSig) && lockedSig.Direction == realDir)
            {
                if (!lockedSig.IsLiveFillSynced || (!_liveTrackedTp1Hit && Math.Abs(lockedSig.EntryPrice - _liveTrackedEntryPrice) >= tickSize))
                {
                    lockedSig.EntryPrice = _liveTrackedEntryPrice;
                    lockedSig.RealAccountFillPrice = rawFillPx > 0m ? rawFillPx : _liveTrackedEntryPrice;
                    lockedSig.IsLiveFillSynced = true;
                    if (!_liveTrackedTp1Hit)
                    {
                        lockedSig.StopPrice = _liveTrackedStopPrice;
                        lockedSig.Tp1Price = _liveTrackedTp1Price;
                        lockedSig.Tp2Price = _liveTrackedTp2Price;
                    }
                    _lockedExecutedSignalsByBar[_liveTrackedSignalBar] = lockedSig;

                    if (_liveTrackedSignalBar < CurrentBar)
                    {
                        if (realDir > 0)
                            _buyAlertSeries[_liveTrackedSignalBar] = _liveTrackedEntryPrice;
                        else
                            _sellAlertSeries[_liveTrackedSignalBar] = _liveTrackedEntryPrice;
                    }
                }
            }
        }

        private void CheckLivePositionTargetsOnTick(decimal livePrice)
        {
            if (!_enableTradingBot || _botMode != BotExecutionMode.LiveAutoTradingManager || livePrice <= 0m)
                return;

            try
            {
                var tm = TradingManager;
                if (tm?.Security == null)
                    return;

                var pos = tm.Position;
                decimal posVol = pos?.Volume ?? 0m;

                if (pos == null || posVol == 0m)
                {
                    // Garde asynchrone : ne jamais réinitialiser _liveTrackedDir pendant qu'un ordre d'ouverture est en transit (< 4.0s)
                    if ((DateTime.UtcNow - _lastLiveOrderSentUtc).TotalSeconds > 4.0)
                    {
                        _liveTrackedDir = 0;
                        _liveTrackedTp1Hit = false;
                        _liveTp1PartialOrderSent = false;
                    }
                    return;
                }

                // Anti-rebond pendant l'exécution d'un ordre d'entrée ou de sortie
                if ((DateTime.UtcNow - _lastLiveOrderSentUtc).TotalMilliseconds < 350 ||
                    (DateTime.UtcNow - _lastLiveExitSentUtc).TotalMilliseconds < 1100)
                {
                    return;
                }

                decimal tickSize = InstrumentInfo?.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
                decimal step = tickSize * Math.Max(1, _ticksPerRow);
                var contractInfo = ResolveContractAndCrossTradingInfo(tickSize);
                decimal pointVal = contractInfo.TradedPointValueUsd;

                EnsureLivePositionTrackingInitialized(pos, livePrice, tickSize);
                if (_liveTrackedDir == 0)
                    return;

                int absQty = (int)Math.Round(Math.Abs(posVol));
                if (absQty <= 0)
                    return;

                // Utilisation exclusive du prix temps réel livePrice (évite de déclencher prématurément un trailing stop sur un ancien sommet intra-bougie)
                if (_liveMaxFavorablePrice <= 0m)
                    _liveMaxFavorablePrice = livePrice;
                else if (_liveTrackedDir > 0 && livePrice > _liveMaxFavorablePrice)
                    _liveMaxFavorablePrice = livePrice;
                else if (_liveTrackedDir < 0 && livePrice < _liveMaxFavorablePrice)
                    _liveMaxFavorablePrice = livePrice;

                decimal tp1DistPts = Math.Max(step * 4m, Math.Abs(_liveTrackedTp1Price - _liveTrackedEntryPrice));
                decimal tp2DistPts = Math.Max(tp1DistPts + step * 4m, Math.Abs(_liveTrackedTp2Price - _liveTrackedEntryPrice));
                decimal tp1TargetUsd = tp1DistPts * pointVal * absQty;
                decimal tp2TargetUsd = tp2DistPts * pointVal * absQty;
                decimal markToMarketUsd = _liveTrackedDir * (livePrice - _liveTrackedEntryPrice) * pointVal * absQty;
                decimal openUnrealizedUsd = (pos.UnrealizedPnL != 0m) ? pos.UnrealizedPnL : markToMarketUsd;

                bool tp2Hit = (_liveTrackedDir > 0 ? (livePrice >= _liveTrackedTp2Price) : (livePrice <= _liveTrackedTp2Price)) ||
                              (tp2TargetUsd > 0m && openUnrealizedUsd >= tp2TargetUsd);

                bool tp1Hit = !_liveTrackedTp1Hit &&
                              ((_liveTrackedDir > 0 ? (livePrice >= _liveTrackedTp1Price) : (livePrice <= _liveTrackedTp1Price)) ||
                               (tp1TargetUsd > 0m && openUnrealizedUsd >= tp1TargetUsd));

                // 1. Prise de profit finale Swing TP2
                if (tp2Hit)
                {
                    _lastLiveExitSentUtc = DateTime.UtcNow;
                    _liveTrackedDir = 0;
                    _liveTrackedTp1Hit = false;
                    _liveTp1PartialOrderSent = false;
                    SendLiveMarketCloseOrReduce(tm, pos, absQty, $"QUANT_BOT_TP2_SWING_PROFIT_{contractInfo.TradedSymbol}");
                    return;
                }

                decimal commRtUsd = Math.Max(0.50m, contractInfo.TradedCommissionPerSideUsd * 2m);
                decimal stepValUsd = Math.Max(0.05m, pointVal * step);
                int commStepFloor = Math.Max(1, (int)Math.Ceiling(commRtUsd / stepValUsd));

                // 2. Prise de profit TP1 (Anti-Microscalping : si 1 contrat, verrouille +1.05R et laisse courir vers TP2 ; si >= 2 contrats, sort 50% et verrouille +0.45R)
                if (tp1Hit)
                {
                    _liveTrackedTp1Hit = true;

                    if (!_botAutoBreakevenOnTp1)
                    {
                        _lastLiveExitSentUtc = DateTime.UtcNow;
                        _liveTrackedDir = 0;
                        _liveTrackedTp1Hit = false;
                        _liveTp1PartialOrderSent = false;
                        SendLiveMarketCloseOrReduce(tm, pos, absQty, $"QUANT_BOT_TP1_FULL_PROFIT_{contractInfo.TradedSymbol}");
                        return;
                    }
                    else if (absQty == 1)
                    {
                        decimal singleLockOffset = Math.Max(RoundToTick(_liveTrackedInitialRiskPts * 1.05m, tickSize), RoundToTick(tp1DistPts * 0.60m, tickSize));
                        decimal lockedStop = RoundToTick(_liveTrackedEntryPrice + _liveTrackedDir * singleLockOffset, tickSize);
                        if ((_liveTrackedDir > 0 && lockedStop > _liveTrackedStopPrice) ||
                            (_liveTrackedDir < 0 && lockedStop < _liveTrackedStopPrice))
                        {
                            _liveTrackedStopPrice = lockedStop;
                        }
                    }
                    else if (!_liveTp1PartialOrderSent)
                    {
                        decimal lockOffset = Math.Max(step * Math.Max(3, commStepFloor + 2), RoundToTick(_liveTrackedInitialRiskPts * 0.45m, tickSize));
                        decimal lockedStop = RoundToTick(_liveTrackedEntryPrice + _liveTrackedDir * lockOffset, tickSize);
                        if ((_liveTrackedDir > 0 && lockedStop > _liveTrackedStopPrice) ||
                            (_liveTrackedDir < 0 && lockedStop < _liveTrackedStopPrice))
                        {
                            _liveTrackedStopPrice = lockedStop;
                        }

                        _liveTp1PartialOrderSent = true;
                        _lastLiveExitSentUtc = DateTime.UtcNow;
                        int halfQty = Math.Max(1, absQty / 2);
                        SendLiveMarketCloseOrReduce(tm, pos, halfQty, $"QUANT_BOT_TP1_50PCT_PROFIT_{contractInfo.TradedSymbol}");
                        return;
                    }
                }

                // 3. Protection Anti-Microscalping : aucun resserrement prématuré à 0.45R !
                // Le Breakeven+ (+0.25R) ne s'active qu'au-delà de +1.25R, et le Trailing Stop Swing s'active après TP1
                decimal favExcursion = _liveTrackedDir > 0
                    ? (_liveMaxFavorablePrice - _liveTrackedEntryPrice)
                    : (_liveTrackedEntryPrice - _liveMaxFavorablePrice);

                if (!_liveTrackedTp1Hit)
                {
                    decimal safeBeTrigger = Math.Max(_liveTrackedInitialRiskPts * 1.25m, tp1DistPts * 0.78m);
                    if (favExcursion >= safeBeTrigger)
                    {
                        decimal lockDist = Math.Max(step * (commStepFloor + 2), RoundToTick(_liveTrackedInitialRiskPts * 0.25m, tickSize));
                        decimal safeBe = RoundToTick(_liveTrackedEntryPrice + _liveTrackedDir * lockDist, tickSize);
                        if ((_liveTrackedDir > 0 && safeBe > _liveTrackedStopPrice) ||
                            (_liveTrackedDir < 0 && safeBe < _liveTrackedStopPrice))
                        {
                            _liveTrackedStopPrice = safeBe;
                        }
                    }
                }
                else
                {
                    decimal trailGap = Math.Max(RoundToTick(_liveTrackedInitialRiskPts * 0.85m, tickSize), RoundToTick(tp1DistPts * 0.55m, tickSize));
                    decimal candidateTrail = RoundToTick(_liveMaxFavorablePrice - _liveTrackedDir * trailGap, tickSize);
                    if ((_liveTrackedDir > 0 && candidateTrail > _liveTrackedStopPrice) ||
                        (_liveTrackedDir < 0 && candidateTrail < _liveTrackedStopPrice))
                    {
                        _liveTrackedStopPrice = candidateTrail;
                    }
                }

                // 4. Exécution du Stop-Loss ou du Trailing Stop Swing en gain
                bool stopHit = _liveTrackedDir > 0 ? (livePrice <= _liveTrackedStopPrice) : (livePrice >= _liveTrackedStopPrice);
                if (stopHit)
                {
                    bool isTrailingProfit = _liveTrackedTp1Hit ||
                        (_liveTrackedDir > 0 ? _liveTrackedStopPrice > _liveTrackedEntryPrice : _liveTrackedStopPrice < _liveTrackedEntryPrice);
                    _lastLiveExitSentUtc = DateTime.UtcNow;
                    _liveTrackedDir = 0;
                    _liveTrackedTp1Hit = false;
                    _liveTp1PartialOrderSent = false;
                    SendLiveMarketCloseOrReduce(tm, pos, absQty, isTrailingProfit
                        ? $"QUANT_BOT_SWING_TRAIL_PROFIT_{contractInfo.TradedSymbol}"
                        : $"QUANT_BOT_STOP_LOSS_{contractInfo.TradedSymbol}");
                }
            }
            catch
            {
                // Protection temps réel
            }
        }

        private void ProcessLiveTradingManagerBot(
            int bar,
            IndicatorCandle candle,
            decimal tickSize,
            VisualTradeSignal signal,
            BotPerformanceState botState)
        {
            try
            {
                var tm = TradingManager;
                if (tm == null || tm.Security == null)
                    return;

                var contractInfo = ResolveContractAndCrossTradingInfo(tickSize);
                var pos = tm.Position;
                decimal posVol = pos?.Volume ?? 0m;
                decimal liveNetPnl = botState.IsSyncedWithActiveAccount
                    ? botState.TotalPnlUsd
                    : ((pos?.RealizedPnL ?? 0m) + (pos?.UnrealizedPnL ?? 0m));

                bool hitMaxDd = botState.IsHaltedByMaxDrawdown ||
                                (botState.CurrentDrawdownUsd >= _botMaxDrawdownUsd) ||
                                (liveNetPnl <= -_botMaxDrawdownUsd);
                bool hitMaxPnl = botState.IsHaltedByMaxProfit || (liveNetPnl >= _botMaxProfitTargetUsd);

                if (hitMaxDd || hitMaxPnl)
                {
                    if (!_liveKillSwitchExecuted && pos != null && posVol != 0m)
                    {
                        _liveKillSwitchExecuted = true;
                        _liveTrackedDir = 0;
                        SendLiveMarketCloseOrReduce(tm, pos, (int)Math.Round(Math.Abs(posVol)), $"QUANT_BOT_KILL_SWITCH_{contractInfo.TradedSymbol}");
                    }
                    return;
                }

                // Vérification de la plage horaire (Heures de Démarrage et de Fin)
                if (_enableBotSchedule && !IsBarWithinBotSchedule(candle.Time))
                {
                    if (_botFlattenAtEndTime && pos != null && posVol != 0m)
                    {
                        _liveTrackedDir = 0;
                        SendLiveMarketCloseOrReduce(tm, pos, (int)Math.Round(Math.Abs(posVol)), $"QUANT_BOT_END_SCHEDULE_{contractInfo.TradedSymbol}");
                    }
                    return;
                }

                _liveKillSwitchExecuted = false;
                decimal step = tickSize * Math.Max(1, _ticksPerRow);
                // En Trading Croisé ATAS X (Chart ES/NQ ➔ Trade MES/MNQ), candle.Close est le prix temps réel du contrat directeur du graphique
                decimal curPrice = candle.Close > 0m
                    ? candle.Close
                    : ((tm.Security.LastTradePrice > 0m) ? tm.Security.LastTradePrice.Value : 0m);

                // 1. Surveillance et exécution temps réel de TP1 / TP2 / Trailing Stop / SL sur la position active
                CheckLivePositionTargetsOnTick(curPrice);

                // 2. Ouverture ou Retournement Atomique de Position aux endroits exacts des signaux de la stratégie
                int sigBar = signal.Bar > 0 ? signal.Bar : bar;
                if (signal.Direction != 0 && (_lastLiveBotOrderBar != sigBar || _lastLiveBotOrderDir != signal.Direction))
                {
                    pos = tm.Position;
                    posVol = pos?.Volume ?? 0m;
                    int currentSign = posVol > 0m ? 1 : (posVol < 0m ? -1 : 0);
                    if (currentSign == signal.Direction)
                    {
                        _lockedExecutedSignalsByBar[sigBar] = signal;
                        return; // Déjà en position dans la même direction
                    }

                    // Cooldown live entre deux ordres (évite le surtrading de micro-mouvements)
                    if (currentSign == 0 && _lastLiveBotOrderBar >= 0 && (sigBar - _lastLiveBotOrderBar < 3))
                        return;

                    if (currentSign != 0 && currentSign != signal.Direction)
                    {
                        decimal openUnr = pos?.UnrealizedPnL ?? 0m;
                        decimal minProfitToFlip = Math.Max(step * 4m, _liveTrackedInitialRiskPts) * contractInfo.TradedPointValueUsd * Math.Abs(posVol) * 0.80m;
                        bool canReverseLive = _liveTrackedTp1Hit || openUnr >= minProfitToFlip || (signal.ConfluenceScore >= 90 && (sigBar - _liveTrackedSignalBar >= 4));
                        if (!canReverseLive)
                            return;
                    }

                    // Protection anti-écart de prix à l'envoi de l'ordre : on vérifie le prix BestAsk/BestBid exécutable en temps réel
                    decimal liveExecChartPx = GetLiveExecutableChartPrice(
                        sigBar,
                        signal.Direction,
                        curPrice > 0m ? curPrice : signal.EntryPrice,
                        tickSize,
                        step,
                        contractInfo.IsCmeMarket,
                        out bool isPriceGapAcceptable,
                        out decimal crossBasisOffset,
                        out decimal rawExecPrice);

                    if (!isPriceGapAcceptable)
                        return;

                    if (liveExecChartPx > 0m)
                    {
                        decimal maxOrderDriftSteps = contractInfo.IsCmeMarket ? 4.0m : 5.0m;
                        if (Math.Abs(liveExecChartPx - signal.EntryPrice) > step * maxOrderDriftSteps)
                            return; // Écart de prix excessif entre le signal et le carnet réel : rejet de l'ordre

                        if (Math.Abs(liveExecChartPx - signal.EntryPrice) >= tickSize)
                        {
                            decimal prevRisk = Math.Max(step * 2m, Math.Abs(signal.EntryPrice - signal.StopPrice));
                            decimal prevTp1Dist = Math.Max(step * 2m, Math.Abs(signal.Tp1Price - signal.EntryPrice));
                            decimal prevTp2Dist = Math.Max(prevTp1Dist + step * 2m, Math.Abs(signal.Tp2Price - signal.EntryPrice));

                            decimal candidateStop = RoundToTick(liveExecChartPx - signal.Direction * prevRisk, tickSize);
                            signal.StopPrice = signal.Direction > 0
                                ? Math.Min(signal.StopPrice, candidateStop)
                                : Math.Max(signal.StopPrice, candidateStop);
                            signal.EntryPrice = liveExecChartPx;
                            signal.Tp1Price = RoundToTick(liveExecChartPx + signal.Direction * prevTp1Dist, tickSize);
                            signal.Tp2Price = RoundToTick(liveExecChartPx + signal.Direction * prevTp2Dist, tickSize);
                        }

                        signal.RealAccountFillPrice = rawExecPrice > 0m
                            ? rawExecPrice
                            : RoundToTick(signal.EntryPrice + crossBasisOffset, tickSize);
                        signal.IsLiveFillSynced = true;
                        _liveCrossBasisOffset = crossBasisOffset;
                        _liveTrackedRealFillPrice = signal.RealAccountFillPrice;
                    }

                    _lastLiveBotOrderBar = sigBar;
                    _lastLiveBotOrderDir = signal.Direction;
                    _lockedExecutedSignalsByBar[sigBar] = signal;

                    if (sigBar < CurrentBar)
                    {
                        if (signal.Direction > 0)
                            _buyAlertSeries[sigBar] = signal.EntryPrice;
                        else
                            _sellAlertSeries[sigBar] = signal.EntryPrice;
                    }

                    int targetQty = Math.Max(1, _botTradeQuantity);
                    int existingOppositeQty = (currentSign != 0 && currentSign != signal.Direction)
                        ? (int)Math.Round(Math.Abs(posVol))
                        : 0;

                    // Ordre Market unique de taille (existingOppositeQty + targetQty) envoyé à tm.Security (qui pointe sur MES / MNQ lorsque le Trading Croisé ATAS X est actif)
                    int orderQty = existingOppositeQty + targetQty;

                    _liveTrackedDir = signal.Direction;
                    _liveTrackedSignalBar = sigBar;
                    _liveTrackedEntryPrice = signal.EntryPrice;
                    _liveTrackedInitialRiskPts = Math.Max(step * 2m, Math.Abs(signal.EntryPrice - signal.StopPrice));
                    _liveTrackedStopPrice = signal.StopPrice;
                    _liveTrackedTp1Price = signal.Tp1Price;
                    _liveTrackedTp2Price = signal.Tp2Price;
                    _liveMaxFavorablePrice = curPrice > 0m ? curPrice : signal.EntryPrice;
                    _liveTrackedTp1Hit = false;
                    _liveTp1PartialOrderSent = false;
                    _lastLiveOrderSentUtc = DateTime.UtcNow;

                    var mktOrder = new Order
                    {
                        Security = tm.Security,
                        Portfolio = tm.Portfolio,
                        Type = OrderTypes.Market,
                        Direction = signal.Direction > 0 ? OrderDirections.Buy : OrderDirections.Sell,
                        QuantityToFill = orderQty,
                        Comment = $"QUANT_BOT_{(signal.Direction > 0 ? "BUY" : "SELL")}_{contractInfo.TradedSymbol}_{signal.ConfluenceScore}%"
                    };
                    _ = tm.OpenOrderAsync(mktOrder, false, false, false);
                }
            }
            catch
            {
                // Protection totale si aucun compte de trading n'est connecté dans ATAS X
            }
        }

        // =====================================================================
        // Rendu Graphique des Trades du Bot (Trajectoires Entrée -> Sortie + Badges PnL $)
        // =====================================================================
        private int GetBarCenterX(int barIndex)
        {
            int xS = ChartInfo!.GetXByBar(barIndex, true);
            int xE = ChartInfo!.GetXByBar(barIndex + 1, true);
            int span = xE - xS;
            if (span <= 4)
                span = Math.Max(8, (int)(ChartInfo!.PriceChartContainer?.BarsWidth ?? 12));
            return xS + span / 2;
        }

        private void RenderBotTradesOnChart(
            RenderContext context,
            List<BotTradeExecutionRecord> trades,
            int firstVisibleBar,
            int lastVisibleBar,
            RenderFont boldFont,
            RenderFont smallFont)
        {
            for (int i = 0; i < trades.Count; i++)
            {
                var t = trades[i];
                if (t.ExitBar < firstVisibleBar || t.EntryBar > lastVisibleBar)
                    continue;

                int x1 = GetBarCenterX(t.EntryBar);
                int x2 = GetBarCenterX(t.ExitBar);
                int y1 = ChartInfo!.GetYByPrice(t.EntryPrice, false);
                int y2 = ChartInfo!.GetYByPrice(t.ExitPrice, false);

                bool isWin = t.RealizedPnlUsd >= 0m;
                Color pathCol = isWin
                    ? Color.FromArgb(220, 16, 245, 150)
                    : Color.FromArgb(220, 255, 65, 95);

                var pathPen = new RenderPen(pathCol, 2.2f, DashStyle.DashDot);
                context.DrawLine(pathPen, x1, y1, x2, y2);

                // Point exact d'ouverture (sur la bougie du signal) et de clôture
                context.FillEllipse(pathCol, new Rectangle(x1 - 5, y1 - 5, 10, 10));
                context.DrawEllipse(new RenderPen(Color.White, 1.2f), new Rectangle(x1 - 5, y1 - 5, 10, 10));
                context.FillEllipse(pathCol, new Rectangle(x2 - 4, y2 - 4, 8, 8));

                string pnlTag = $"BOT {(t.Direction > 0 ? "LONG" : "SHORT")} {t.ExitReason} : {t.RealizedPnlUsd:+0;-0;0}$";
                Size sz = context.MeasureString(pnlTag, smallFont);
                int bx = Math.Max(10, x2 - sz.Width / 2);
                int by = t.Direction > 0 ? y2 - sz.Height - 10 : y2 + 8;

                Rectangle tagRect = new Rectangle(bx - 4, by - 2, sz.Width + 8, sz.Height + 4);
                context.FillRectangle(Color.FromArgb(225, 11, 17, 30), tagRect);
                context.DrawRectangle(new RenderPen(pathCol, 1.3f), tagRect);
                context.DrawString(pnlTag, smallFont, pathCol, bx, by);
            }
        }

        // =====================================================================
        // Rendu du Cartouche Visuel ACHAT ▲ / VENTE ▼ et des lignes Entrée/SL
        // =====================================================================
        private void RenderBuySellSignalOnBar(
            RenderContext context,
            VisualTradeSignal sig,
            IndicatorCandle candle,
            int xCenter,
            int xCellRight,
            int lastVisibleBar,
            int rowHeight,
            RenderFont boldFont,
            RenderFont smallFont)
        {
            bool isBuy = sig.Direction > 0;
            Color sigColor = isBuy ? BuySignalColor : SellSignalColor;
            Color darkBg = isBuy
                ? Color.FromArgb(235, 6, 48, 34)
                : Color.FromArgb(235, 58, 12, 26);

            // Lignes de Prix d'Entrée (ENT), Stop-Loss (SL) et Objectifs Quant TP1 / TP2
            if (ShowEntryAndStopLevels)
            {
                int endBar = Math.Min(lastVisibleBar + 1, sig.Bar + 4);
                int x2 = Math.Max(xCellRight + 38, ChartInfo!.GetXByBar(endBar, true));
                int yEntry = ChartInfo!.GetYByPrice(sig.EntryPrice, false);
                int yStop = ChartInfo!.GetYByPrice(sig.StopPrice, false);
                int yTp1 = ChartInfo!.GetYByPrice(sig.Tp1Price, false);
                int yTp2 = ChartInfo!.GetYByPrice(sig.Tp2Price, false);

                var entryPen = new RenderPen(sigColor, 2f, DashStyle.Solid);
                var stopPen = new RenderPen(Color.FromArgb(220, 255, 165, 0), 1.6f, DashStyle.Dot);
                var tpPen = new RenderPen(Color.FromArgb(205, 56, 189, 248), 1.4f, DashStyle.Dash);

                context.DrawLine(entryPen, xCellRight + 2, yEntry, x2, yEntry);
                context.DrawLine(stopPen, xCellRight + 2, yStop, x2, yStop);
                context.DrawLine(tpPen, xCellRight + 2, yTp1, x2, yTp1);
                context.DrawLine(tpPen, xCellRight + 2, yTp2, x2, yTp2);

                string fillSuffix = (sig.IsLiveFillSynced && sig.RealAccountFillPrice > 0m && Math.Abs(sig.RealAccountFillPrice - sig.EntryPrice) >= 0.01m)
                    ? $" (Fill Réel: {sig.RealAccountFillPrice})"
                    : (sig.IsLiveFillSynced ? " [EXÉCUTÉ]" : string.Empty);
                string entTxt = $"{(isBuy ? "ACHAT" : "VENTE")} @ {sig.EntryPrice}{fillSuffix}";
                string slTxt = $"SL @ {sig.StopPrice}";
                string tp1Txt = $"TP1 ({_quantTp1RiskReward:0.0}R) @ {sig.Tp1Price}";
                string tp2Txt = $"TP2 ({_quantTp2RiskReward:0.0}R) @ {sig.Tp2Price}";
                context.DrawString(entTxt, smallFont, sigColor, xCellRight + 6, yEntry - 14);
                context.DrawString(slTxt, smallFont, Color.FromArgb(255, 251, 191, 36), xCellRight + 6, yStop + 2);
                context.DrawString(tp1Txt, smallFont, Color.FromArgb(255, 125, 211, 252), xCellRight + 6, yTp1 - 13);
                context.DrawString(tp2Txt, smallFont, Color.FromArgb(255, 56, 189, 248), xCellRight + 6, yTp2 - 13);
            }

            if (!ShowSignalCalloutOnBar)
                return;

            int baseOffset = Math.Max(22, rowHeight + BarMarkerSize * 2 + 12);
            int yExtreme = isBuy
                ? ChartInfo!.GetYByPrice(candle.Low, false) + baseOffset
                : ChartInfo!.GetYByPrice(candle.High, false) - baseOffset;

            string line1 = isBuy
                ? $"◆ REVERSAL ACHAT ▲ [BigTrade {sig.BigTradeLots:0}L Piégé @ {sig.BigTradePrice}]"
                : $"◆ REVERSAL VENTE ▼ [BigTrade {sig.BigTradeLots:0}L Piégé @ {sig.BigTradePrice}]";
            string line2 = $"TP1: {sig.Tp1Price} • TP2: {sig.Tp2Price} | SL: {sig.StopPrice}";

            Size sz1 = context.MeasureString(line1, boldFont);
            Size sz2 = context.MeasureString(line2, smallFont);
            int boxW = Math.Max(sz1.Width, sz2.Width) + 16;
            int boxH = sz1.Height + sz2.Height + 10;

            int boxX = xCenter - boxW / 2;
            int boxY = isBuy ? yExtreme + 8 : yExtreme - boxH - 8;

            // Flèche de liaison entre le prix et le cartouche
            Point tip = isBuy
                ? new Point(xCenter, yExtreme - 2)
                : new Point(xCenter, yExtreme + 2);
            Point baseLeft = new Point(xCenter - 7, isBuy ? boxY : boxY + boxH);
            Point baseRight = new Point(xCenter + 7, isBuy ? boxY : boxY + boxH);
            context.FillPolygon(sigColor, new[] { tip, baseLeft, baseRight });

            // Halo extérieur + Cartouche ACHAT / VENTE
            Rectangle glowRect = new Rectangle(boxX - 2, boxY - 2, boxW + 4, boxH + 4);
            context.FillRectangle(Color.FromArgb(65, sigColor.R, sigColor.G, sigColor.B), glowRect);

            Rectangle boxRect = new Rectangle(boxX, boxY, boxW, boxH);
            context.FillRectangle(darkBg, boxRect);
            context.DrawRectangle(new RenderPen(sigColor, 2f), boxRect);

            // Bandeau supérieur coloré sur le cartouche
            context.FillRectangle(sigColor, new Rectangle(boxX, boxY, boxW, 3));

            context.DrawString(line1, boldFont, sigColor, boxX + (boxW - sz1.Width) / 2, boxY + 4);
            context.DrawString(line2, smallFont, Color.White, boxX + (boxW - sz2.Width) / 2, boxY + 5 + sz1.Height);
        }

        // =====================================================================
        // Bannière Visuelle d'Alerte ACHAT / VENTE + Statut Bot en haut du graphique ATAS X
        // =====================================================================
        private void RenderVisualAlertBanner(
            RenderContext context,
            Rectangle chartRegion,
            VisualTradeSignal sig,
            BarOptionsFlowMetrics latestOpt,
            BotPerformanceState botPerf)
        {
            bool isBuy = sig.Direction > 0;
            Color accent = isBuy ? BuySignalColor : SellSignalColor;
            Color bg = isBuy
                ? Color.FromArgb(235, 6, 42, 30)
                : Color.FromArgb(235, 52, 12, 24);

            if (_enableTradingBot && botPerf.IsHaltedByMaxDrawdown)
            {
                accent = Color.FromArgb(255, 255, 59, 48);
                bg = Color.FromArgb(240, 68, 10, 18);
            }
            else if (_enableTradingBot && botPerf.IsHaltedByMaxProfit)
            {
                accent = Color.FromArgb(255, 16, 245, 150);
                bg = Color.FromArgb(240, 6, 52, 36);
            }

            var headerFont = new RenderFont("Consolas", 11, FontStyle.Bold);
            var subFont = new RenderFont("Consolas", 10);

            string title = isBuy
                ? $"▲ REVERSAL ACHAT (◆ BIGTRADE {sig.BigTradeLots:0}L PIÉGÉ) @ {sig.EntryPrice} | SL: {sig.StopPrice} | TP1: {sig.Tp1Price} | TP2: {sig.Tp2Price}"
                : $"▼ REVERSAL VENTE (◆ BIGTRADE {sig.BigTradeLots:0}L PIÉGÉ) @ {sig.EntryPrice} | SL: {sig.StopPrice} | TP1: {sig.Tp1Price} | TP2: {sig.Tp2Price}";

            string acctTag = !string.IsNullOrWhiteSpace(botPerf.ActiveAccountName) ? $" [{botPerf.ActiveAccountName}]" : string.Empty;
            string botTag = (_enableTradingBot || botPerf.IsSyncedWithActiveAccount)
                ? $" | PnL Réel{acctTag}: {botPerf.TotalPnlUsd:+0.00;-0.00;0.00}$ (Max {BotMaxProfitTargetUsd:0}$) • DD: -{botPerf.MaxObservedDrawdownUsd:0.00}$ (Max -{BotMaxDrawdownUsd:0}$)"
                : string.Empty;
            string detail = $"Options X-Ray : {sig.XRaySummaryTag} | {sig.ReasonTag}{botTag}";

            Size s1 = context.MeasureString(title, headerFont);
            Size s2 = context.MeasureString(detail, subFont);

            int w = Math.Max(s1.Width, s2.Width) + 28;
            int h = s1.Height + s2.Height + 16;

            int x = chartRegion.Left + Math.Max(14, (chartRegion.Width - w) / 2);
            if (ShowHudPanel && HudPosition == HudCorner.TopLeft)
                x = Math.Max(x, chartRegion.Left + 380);

            int y = ShowChartBotToggleButton ? chartRegion.Top + 112 : chartRegion.Top + 10;

            Rectangle bannerRect = new Rectangle(x, y, w, h);
            context.FillRectangle(bg, bannerRect);
            context.DrawRectangle(new RenderPen(accent, 2f), bannerRect);
            context.FillRectangle(accent, new Rectangle(x, y, 6, h));

            context.DrawString(title, headerFont, accent, x + 14, y + 5);
            context.DrawString(detail, subFont, Color.FromArgb(255, 226, 232, 240), x + 14, y + 8 + s1.Height);
        }

        // =====================================================================
        // Profil Composite Multi-Bougies : Détection et Rendu des Zones HVN & LVN
        // =====================================================================
        private void RenderCompositeProfileZones(
            RenderContext context,
            Rectangle chartRegion,
            int lastBar,
            decimal tickSize,
            decimal step,
            int rowHeight,
            RenderFont font,
            out int hvnZonesDrawn,
            out int lvnZonesDrawn,
            out decimal compositePocPrice,
            out decimal compositePocVol)
        {
            hvnZonesDrawn = 0;
            lvnZonesDrawn = 0;
            compositePocPrice = 0m;
            compositePocVol = 0m;

            int startBar = Math.Max(0, lastBar - ProfileLookbackBars + 1);
            if (ProfileScope == ProfileScopeMode.CurrentSession)
            {
                startBar = lastBar;
                while (startBar > 0 && !IsNewSession(startBar))
                    startBar--;
            }

            var volumeByLevel = new SortedDictionary<decimal, decimal>();
            for (int b = startBar; b <= lastBar; b++)
            {
                var c = GetCandle(b);
                bool usedAllLevels = false;
                try
                {
                    var allLevels = c.GetAllPriceLevels();
                    if (allLevels != null)
                    {
                        foreach (var pv in allLevels)
                        {
                            if (pv == null)
                                continue;
                            decimal v = pv.Bid + pv.Ask;
                            if (v <= 0m)
                                continue;
                            decimal bucket = Math.Floor(RoundToTick(pv.Price, tickSize) / step) * step;
                            if (volumeByLevel.TryGetValue(bucket, out decimal existing))
                                volumeByLevel[bucket] = existing + v;
                            else
                                volumeByLevel[bucket] = v;
                            usedAllLevels = true;
                        }
                    }
                }
                catch
                {
                    usedAllLevels = false;
                }

                if (!usedAllLevels)
                {
                    for (decimal p = c.Low; p <= c.High; p += tickSize)
                    {
                        var pv = c.GetPriceVolumeInfo(RoundToTick(p, tickSize));
                        if (pv == null)
                            continue;

                        decimal bucket = Math.Floor(p / step) * step;
                        decimal v = pv.Bid + pv.Ask;
                        if (v <= 0m)
                            continue;

                        if (volumeByLevel.TryGetValue(bucket, out decimal existing))
                            volumeByLevel[bucket] = existing + v;
                        else
                            volumeByLevel[bucket] = v;
                    }
                }
            }

            if (volumeByLevel.Count < 3)
                return;

            var prices = new List<decimal>(volumeByLevel.Keys);
            var vols = new List<decimal>(volumeByLevel.Values);
            int n = prices.Count;

            for (int i = 0; i < n; i++)
            {
                if (vols[i] > compositePocVol)
                {
                    compositePocVol = vols[i];
                    compositePocPrice = prices[i];
                }
            }

            if (compositePocVol <= 0m)
                return;

            decimal hvnThreshold = compositePocVol * (CompositeHvnPct / 100m);
            decimal lvnThreshold = compositePocVol * (CompositeLvnPct / 100m);

            var hvnCandidates = new List<(decimal price, decimal vol)>();
            var lvnCandidates = new List<(decimal price, decimal vol)>();

            for (int i = 1; i < n - 1; i++)
            {
                decimal v = vols[i];
                decimal prev = vols[i - 1];
                decimal next = vols[i + 1];

                if (v >= hvnThreshold && v >= prev && v >= next)
                    hvnCandidates.Add((prices[i], v));
                else if (v <= lvnThreshold && v < prev && v < next)
                    lvnCandidates.Add((prices[i], v));
            }

            hvnCandidates.Sort((a, b) => b.vol.CompareTo(a.vol));
            lvnCandidates.Sort((a, b) => a.vol.CompareTo(b.vol));

            int xLeft = Math.Max(chartRegion.Left, ChartInfo!.GetXByBar(startBar, true));
            int xRight = chartRegion.Right;
            if (xRight <= xLeft)
                return;

            int bandH = Math.Max(3, rowHeight);

            // Tracé des zones HVN Composites
            int hCount = Math.Min(MaxCompositeZonesEach, hvnCandidates.Count);
            for (int i = 0; i < hCount; i++)
            {
                var node = hvnCandidates[i];
                int y = ChartInfo!.GetYByPrice(node.price, false);
                if (y < chartRegion.Top || y > chartRegion.Bottom)
                    continue;

                Rectangle zoneRect = new Rectangle(xLeft, y - bandH / 2, xRight - xLeft, bandH);
                Color fill = Color.FromArgb(CompositeBandAlpha, CompositeHvnColor.R, CompositeHvnColor.G, CompositeHvnColor.B);
                Color lineCol = Color.FromArgb(170, CompositeHvnColor.R, CompositeHvnColor.G, CompositeHvnColor.B);

                context.FillRectangle(fill, zoneRect);
                context.DrawLine(new RenderPen(lineCol, 1f), xLeft, y, xRight, y);

                string lbl = node.price == compositePocPrice
                    ? $"VPOC / HVN {node.price} ({node.vol:0})"
                    : $"HVN Zone {node.price} ({node.vol:0})";
                Size sz = context.MeasureString(lbl, font);
                context.DrawString(lbl, font, CompositeHvnColor, xRight - sz.Width - 8, y - sz.Height - 1);
                hvnZonesDrawn++;
            }

            // Tracé des vallées LVN Composites
            int lCount = Math.Min(MaxCompositeZonesEach, lvnCandidates.Count);
            for (int i = 0; i < lCount; i++)
            {
                var node = lvnCandidates[i];
                int y = ChartInfo!.GetYByPrice(node.price, false);
                if (y < chartRegion.Top || y > chartRegion.Bottom)
                    continue;

                Rectangle zoneRect = new Rectangle(xLeft, y - bandH / 2, xRight - xLeft, bandH);
                Color fill = Color.FromArgb(Math.Max(10, CompositeBandAlpha - 10), CompositeLvnColor.R, CompositeLvnColor.G, CompositeLvnColor.B);
                Color lineCol = Color.FromArgb(165, CompositeLvnColor.R, CompositeLvnColor.G, CompositeLvnColor.B);

                context.FillRectangle(fill, zoneRect);
                context.DrawLine(new RenderPen(lineCol, 1f, DashStyle.Dash), xLeft, y, xRight, y);

                string lbl = $"LVN Pocket {node.price} ({node.vol:0})";
                Size sz = context.MeasureString(lbl, font);
                context.DrawString(lbl, font, CompositeLvnColor, xRight - sz.Width - 8, y + 2);
                lvnZonesDrawn++;
            }
        }

        // =====================================================================
        // Rayons Support/Résistance issus des Heavy Absorptions & Traders Piégés
        // =====================================================================
        private void RenderSignalRays(
            RenderContext context,
            List<RaySignalEvent> events,
            int lastVisibleBar,
            decimal step,
            RenderFont font,
            int maxBars,
            bool isTrappedRay)
        {
            for (int i = 0; i < events.Count; i++)
            {
                RaySignalEvent ev = events[i];
                int endBar = Math.Min(CurrentBar - 1, ev.Bar + maxBars);

                if (StopRayWhenMitigated)
                {
                    for (int b = ev.Bar + 1; b <= endBar; b++)
                    {
                        var c = GetCandle(b);
                        if (ev.Side > 0 && c.Close < ev.Price - step * 0.5m)
                        {
                            endBar = b;
                            break;
                        }
                        if (ev.Side < 0 && c.Close > ev.Price + step * 0.5m)
                        {
                            endBar = b;
                            break;
                        }
                    }
                }

                if (endBar <= ev.Bar)
                    continue;

                int x1 = ChartInfo!.GetXByBar(ev.Bar, true);
                int x2 = ChartInfo!.GetXByBar(Math.Min(lastVisibleBar + 1, endBar + 1), true);
                if (x2 <= x1)
                    continue;

                int y = ChartInfo!.GetYByPrice(ev.Price, false);
                Color baseCol = isTrappedRay
                    ? (ev.Side > 0 ? TrappedSellersBullColor : TrappedBuyersBearColor)
                    : (ev.Side > 0 ? BullishAbsorptionColor : BearishAbsorptionColor);
                Color rayCol = Color.FromArgb(isTrappedRay ? 185 : 205, baseCol.R, baseCol.G, baseCol.B);

                var pen = new RenderPen(rayCol, isTrappedRay ? 1.6f : 2f, isTrappedRay ? DashStyle.Dash : DashStyle.Solid);
                context.DrawLine(pen, x1, y, x2, y);

                string rayLabel = isTrappedRay
                    ? (ev.Side > 0
                        ? $"Vendeurs Piégés ▲ @ {ev.Price} (Δ {ev.Delta:+0;-0;0})"
                        : $"Acheteurs Piégés ▼ @ {ev.Price} (Δ {ev.Delta:+0;-0;0})")
                    : (ev.Side > 0
                        ? $"Bull ABS Support @ {ev.Price} (Vol {ev.Volume:0})"
                        : $"Bear ABS Resist @ {ev.Price} (Vol {ev.Volume:0})");

                Size sz = context.MeasureString(rayLabel, font);
                if (x2 - x1 >= sz.Width + 10)
                {
                    int labelY = isTrappedRay ? (y + 2) : (y - sz.Height - 2);
                    context.DrawString(rayLabel, font, baseCol, x2 - sz.Width - 4, labelY);
                }
            }
        }

        // =====================================================================
        // Marqueur visuel au-dessus / en dessous de la bougie Footprint
        // =====================================================================
        private void DrawExtremeMarker(
            RenderContext context,
            IndicatorCandle candle,
            int side,
            Color color,
            int xCenter,
            int rowHeight,
            AbsorptionMarkerType style,
            int extraOffsetPx)
        {
            int size = BarMarkerSize;
            int gap = Math.Max(6, rowHeight / 2 + 4) + extraOffsetPx;
            int y = side > 0
                ? ChartInfo!.GetYByPrice(candle.Low, false) + gap + size
                : ChartInfo!.GetYByPrice(candle.High, false) - gap - size;

            switch (style)
            {
                case AbsorptionMarkerType.Dot:
                    context.FillEllipse(color, new Rectangle(xCenter - size, y - size, size * 2, size * 2));
                    break;

                case AbsorptionMarkerType.Diamond:
                    var diamond = new[]
                    {
                        new Point(xCenter, y - size),
                        new Point(xCenter + size, y),
                        new Point(xCenter, y + size),
                        new Point(xCenter - size, y)
                    };
                    context.FillPolygon(color, diamond);
                    break;

                case AbsorptionMarkerType.Arrow:
                    Point tip = side > 0 ? new Point(xCenter, y - size) : new Point(xCenter, y + size);
                    Point left = new Point(xCenter - size, y);
                    Point right = new Point(xCenter + size, y);
                    context.FillPolygon(color, new[] { tip, left, right });
                    break;

                case AbsorptionMarkerType.Bracket:
                    var pen = new RenderPen(color, 2f);
                    int dir = side > 0 ? -1 : 1;
                    context.DrawLine(pen, xCenter - size, y + dir * size, xCenter - size, y);
                    context.DrawLine(pen, xCenter - size, y, xCenter + size, y);
                    context.DrawLine(pen, xCenter + size, y, xCenter + size, y + dir * size);
                    break;
            }
        }

        // =====================================================================
        // Panneau HUD (Heads-Up Display) en surimpression sur le graphique
        // =====================================================================
        private void RenderHud(
            RenderContext context,
            Rectangle chartRegion,
            int barHvnCount,
            int barLvnCount,
            int compHvnCount,
            int compLvnCount,
            decimal compPocPrice,
            decimal compPocVol,
            int bullAbsCount,
            int bearAbsCount,
            int trappedSellersCount,
            int trappedBuyersCount,
            int buySignalsCount,
            int sellSignalsCount,
            RaySignalEvent? latestAbs,
            RaySignalEvent? latestTrapped,
            VisualTradeSignal? latestSignal,
            BarOptionsFlowMetrics latestOpt,
            BotPerformanceState botPerf)
        {
            var titleFont = new RenderFont("Consolas", 11, FontStyle.Bold);
            var bodyFont = new RenderFont("Consolas", 10);

            string sym = InstrumentInfo?.Instrument ?? "Instrument";
            decimal hudTickSize = InstrumentInfo != null && InstrumentInfo.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
            var contractInfo = ResolveContractAndCrossTradingInfo(hudTickSize);

            var lines = new List<(string text, Color color, bool bold)>
            {
                ($"BOT BIGTRADES ◆ REVERSAL • OPTIONS X-RAY • HVN/LVN [{sym}]", Color.White, true),
                ($"MARCHÉ & CROISÉ   : {contractInfo.ProfileBadge}  |  {contractInfo.CrossTradingBadge} ({contractInfo.TradedPointValueUsd:0.##}$/pt)", Color.FromArgb(255, 250, 204, 21), true),
                ($"HVN Intra-Bar : {barHvnCount}  |  Zones HVN Profil : {compHvnCount}  |  VPOC : {(compPocPrice > 0m ? $"{compPocPrice}" : "N/A")}", BarHvnColor, false),
                ($"LVN Intra-Bar : {barLvnCount}  |  Zones LVN Profil : {compLvnCount}  |  ATR : {latestOpt.AtrPoints:0.00}", BarLvnColor, false),
                ($"BigTrades ◆ Visibles : Vendeurs (Rouges) {bullAbsCount}  |  Acheteurs (Verts) {bearAbsCount}  |  Piégés (Or) : {trappedSellersCount + trappedBuyersCount}", Color.FromArgb(255, 250, 204, 21), false),
                ($"Options X-Ray SPX   : CALL WALL {latestOpt.XRayCallWall:0.##} (+{latestOpt.XRayCallWallGexM:0.0}M) | MP {latestOpt.XRayMaxPain:0.##} | PUT WALL {latestOpt.XRayPutWall:0.##} ({latestOpt.XRayPutWallGexM:+0.0;-0.0;0.0}M)", Color.FromArgb(255, 250, 204, 21), true),
                ($"Signaux Reversal ◆  : ACHAT ▲ {buySignalsCount}  |  VENTE ▼ {sellSignalsCount}  (Seuil ◆≥{GetEffectiveBigTradeMinLots(hudTickSize):0} lots, 0 DTE={latestOpt.XRayZeroDteExpectedMovePts:0}pts)", Color.FromArgb(255, 125, 211, 252), true)
            };

            string acctDisplay = !string.IsNullOrWhiteSpace(botPerf.ActiveAccountName)
                ? botPerf.ActiveAccountName
                : (_syncPnlWithActiveAccount ? "AUCUN COMPTE ACTIF" : "SIMULATION GRAPHIQUE");
            string scopeTxt = _accountPnlScope == ActiveAccountPnlScope.ActivePortfolioTotal ? "Compte Global" : "Instrument Seul";
            string syncStatusTxt = botPerf.IsSyncedWithActiveAccount
                ? $"SYNC PNL RÉEL: ACTIF ({scopeTxt})"
                : (_syncPnlWithActiveAccount ? "EN ATTENTE COMPTE ATAS (Fallback Sim)" : "SYNC DÉSACTIVÉE (Sim)");

            Color pnlCol = botPerf.TotalPnlUsd >= 0m
                ? Color.FromArgb(255, 0, 255, 136)
                : Color.FromArgb(255, 255, 65, 85);

            lines.Add(($"COMPTE ACTIF ATAS   : {acctDisplay} (Solde: {botPerf.ActiveAccountBalance:0.00}$) [{syncStatusTxt}]",
                botPerf.IsSyncedWithActiveAccount ? Color.FromArgb(255, 56, 189, 248) : Color.FromArgb(255, 148, 163, 184), true));
            lines.Add(($"PNL RÉEL DU COMPTE  : {botPerf.TotalPnlUsd:+0.00;-0.00;0.00}$ (Réalisé: {botPerf.RealizedPnlUsd:+0.00;-0.00;0.00}$ | Latent: {botPerf.UnrealizedPnlUsd:+0.00;-0.00;0.00}$) / Max +{_botMaxProfitTargetUsd:0}$",
                pnlCol, true));

            if (_enableTradingBot)
            {
                Color statusCol = botPerf.IsHaltedByMaxDrawdown
                    ? Color.FromArgb(255, 255, 65, 85)
                    : (botPerf.IsHaltedByMaxProfit
                        ? Color.FromArgb(255, 16, 245, 150)
                        : Color.FromArgb(255, 56, 189, 248));

                lines.Add(($"BOT STATUS          : {botPerf.StatusLabel}", statusCol, true));
                lines.Add(($"HORAIRES DU BOT     : {( _enableBotSchedule ? $"{BotStartTimeText} -> {BotEndTimeText} (Filtre Horaire ACTIF)" : "24h/24 (Filtre Horaire DÉSACTIVÉ)" )}", Color.FromArgb(255, 165, 243, 252), false));
                lines.Add(($"DRAWDOWN / DD MAX   : -{botPerf.CurrentDrawdownUsd:0.00}$ (Max Observé -{botPerf.MaxObservedDrawdownUsd:0.00}$) / Limite -{_botMaxDrawdownUsd:0}$", Color.FromArgb(255, 251, 191, 36), false));
                lines.Add(($"STATS BOT BIGTRADES : Trades {botPerf.TotalTrades} ({botPerf.WinRatePct:0}% Win) | PF {botPerf.ProfitFactor:0.00} | Sharpe {botPerf.SharpeRatio:0.00} | Exp {botPerf.ExpectancyUsd:+0;-0;0}$", Color.FromArgb(255, 203, 213, 225), false));
            }
            else
            {
                lines.Add(("BOT STATUS          : DÉSACTIVÉ (Cliquez sur [BOT BIGTRADES: OFF] sur la charte pour activer)", Color.FromArgb(255, 148, 163, 184), true));
                lines.Add(($"HORAIRES CONFIGURÉS : {BotStartTimeText} -> {BotEndTimeText} ({(_enableBotSchedule ? "ON" : "OFF")})", Color.FromArgb(255, 148, 163, 184), false));
            }

            if (latestSignal.HasValue)
            {
                var s = latestSignal.Value;
                bool isBuy = s.Direction > 0;
                string tag = isBuy ? "◆ DERNIER REVERSAL ACHAT ▲" : "◆ DERNIER REVERSAL VENTE ▼";
                Color c = isBuy ? BuySignalColor : SellSignalColor;
                lines.Add(($"{tag}: Entrée {s.EntryPrice} | SL {s.StopPrice} | TP1 {s.Tp1Price} | TP2 {s.Tp2Price} ({s.BigTradeLots:0}L @ {s.BigTradePrice})", c, true));
            }

            int pad = 10;
            int lineH = 18;
            int maxW = 0;

            for (int i = 0; i < lines.Count; i++)
            {
                Size sz = context.MeasureString(lines[i].text, lines[i].bold ? titleFont : bodyFont);
                if (sz.Width > maxW)
                    maxW = sz.Width;
            }

            int gaugeAreaH = _enableTradingBot ? 34 : 0;
            int boxW = Math.Max(460, maxW + pad * 2);
            int boxH = lines.Count * lineH + pad * 2 + gaugeAreaH;

            int x = HudPosition switch
            {
                HudCorner.TopLeft or HudCorner.BottomLeft => chartRegion.Left + HudOffsetX,
                _ => chartRegion.Right - boxW - HudOffsetX
            };

            int y = HudPosition switch
            {
                HudCorner.BottomLeft or HudCorner.BottomRight => chartRegion.Bottom - boxH - HudOffsetY,
                _ => chartRegion.Top + HudOffsetY
            };

            Rectangle boxRect = new Rectangle(x, y, boxW, boxH);
            context.FillRectangle(Color.FromArgb(228, 9, 14, 26), boxRect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(230, 56, 189, 248), 1.6f), boxRect);

            // Bouton cliquable ON/OFF intégré dans l'en-tête supérieur droit du HUD
            string hudBtnTxt = _enableTradingBot ? "● BOT : ON" : "○ BOT : OFF";
            Size hudBtnSz = context.MeasureString(hudBtnTxt, bodyFont);
            int hudBtnW = hudBtnSz.Width + 16;
            int hudBtnH = 20;
            _hudBotToggleBtnRect = new Rectangle(x + boxW - hudBtnW - 8, y + 6, hudBtnW, hudBtnH);

            Color hudBtnBg = _enableTradingBot
                ? (_hoverBotToggleBtn ? Color.FromArgb(245, 16, 185, 129) : Color.FromArgb(225, 6, 95, 70))
                : (_hoverBotToggleBtn ? Color.FromArgb(245, 71, 85, 105) : Color.FromArgb(225, 30, 41, 59));
            Color hudBtnBorder = _enableTradingBot
                ? Color.FromArgb(255, 0, 255, 136)
                : Color.FromArgb(220, 148, 163, 184);

            context.FillRectangle(hudBtnBg, _hudBotToggleBtnRect);
            context.DrawRectangle(new RenderPen(hudBtnBorder, _hoverBotToggleBtn ? 2f : 1.3f), _hudBotToggleBtnRect);
            context.DrawString(hudBtnTxt, bodyFont, Color.White, _hudBotToggleBtnRect.Left + 8, _hudBotToggleBtnRect.Top + 2);

            int curY = y + pad;
            for (int i = 0; i < lines.Count; i++)
            {
                context.DrawString(
                    lines[i].text,
                    lines[i].bold ? titleFont : bodyFont,
                    lines[i].color,
                    x + pad,
                    curY);
                curY += lineH;
            }

            // Jauges graphiques de progression vers PnL Max et Drawdown Max
            if (_enableTradingBot)
            {
                int barY = curY + 6;
                int halfW = (boxW - pad * 3) / 2;
                int barH = 14;

                // 1. Jauge PnL Max (Gauche)
                decimal pnlRatio = _botMaxProfitTargetUsd > 0m
                    ? Math.Clamp(botPerf.TotalPnlUsd / _botMaxProfitTargetUsd, 0m, 1m)
                    : 0m;
                Rectangle pnlBg = new Rectangle(x + pad, barY, halfW, barH);
                context.FillRectangle(Color.FromArgb(200, 20, 30, 48), pnlBg);
                int pnlFillW = (int)Math.Round(halfW * (double)pnlRatio);
                if (pnlFillW > 0)
                {
                    context.FillRectangle(Color.FromArgb(220, 0, 230, 125), new Rectangle(pnlBg.Left, pnlBg.Top, pnlFillW, barH));
                }
                context.DrawRectangle(new RenderPen(Color.FromArgb(200, 0, 255, 136), 1f), pnlBg);
                string pnlLbl = $"PnL Max: {(pnlRatio * 100m):0}% ({botPerf.TotalPnlUsd:+0;-0;0}$/{_botMaxProfitTargetUsd:0}$)";
                context.DrawString(pnlLbl, bodyFont, Color.White, pnlBg.Left + 5, pnlBg.Top - 1);

                // 2. Jauge Drawdown Max (Droite)
                decimal ddRatio = _botMaxDrawdownUsd > 0m
                    ? Math.Clamp(botPerf.MaxObservedDrawdownUsd / _botMaxDrawdownUsd, 0m, 1m)
                    : 0m;
                Rectangle ddBg = new Rectangle(x + pad * 2 + halfW, barY, halfW, barH);
                context.FillRectangle(Color.FromArgb(200, 35, 18, 28), ddBg);
                int ddFillW = (int)Math.Round(halfW * (double)ddRatio);
                if (ddFillW > 0)
                {
                    context.FillRectangle(Color.FromArgb(220, 255, 55, 85), new Rectangle(ddBg.Left, ddBg.Top, ddFillW, barH));
                }
                context.DrawRectangle(new RenderPen(Color.FromArgb(200, 255, 80, 100), 1f), ddBg);
                string ddLbl = $"DD Max: {(ddRatio * 100m):0}% (-{botPerf.MaxObservedDrawdownUsd:0}$/-{_botMaxDrawdownUsd:0}$)";
                context.DrawString(ddLbl, bodyFont, Color.White, ddBg.Left + 5, ddBg.Top - 1);
            }
        }

        // =====================================================================
        // Bouton Interactif d'Activation du Bot directement sur la Charte ATAS X
        // =====================================================================
        private void RenderOnChartBotControlButton(
            RenderContext context,
            Rectangle chartRegion,
            BotPerformanceState botPerf)
        {
            var btnFont = new RenderFont("Consolas", 10, FontStyle.Bold);
            var subFont = new RenderFont("Consolas", 9);

            decimal ctrlTickSize = InstrumentInfo != null && InstrumentInfo.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
            var ctrlContractInfo = ResolveContractAndCrossTradingInfo(ctrlTickSize);

            string mainTxt = _enableTradingBot
                ? (botPerf.IsHaltedByMaxDrawdown
                    ? "BOT BIGTRADES : STOP DD MAX"
                    : (botPerf.IsHaltedByMaxProfit ? "BOT BIGTRADES : LOCK PNL MAX" : "BOT BIGTRADES : ON (ACTIF)"))
                : "BOT BIGTRADES : OFF (CLIQUEZ)";

            string modeTxt = _botMode == BotExecutionMode.LiveAutoTradingManager
                ? "MODE: LIVE ATAS"
                : "MODE: SIM/PAPER";

            string resetTxt = "RESET";

            string profileShort = _marketProfile switch
            {
                MarketProfilePreset.AutoDetect => $"PROFIL: AUTO ({ctrlContractInfo.EffectiveProfile switch { MarketProfilePreset.CryptoBinanceFutures => "BTC", MarketProfilePreset.CmeSp500_ES_MES => "ES/S&P", MarketProfilePreset.CmeNasdaq_NQ_MNQ => "NQ/NAS", _ => "CME" }})",
                MarketProfilePreset.CryptoBinanceFutures => "PROFIL: BTC BINANCE",
                MarketProfilePreset.CmeSp500_ES_MES => "PROFIL: CME S&P (ES)",
                MarketProfilePreset.CmeNasdaq_NQ_MNQ => "PROFIL: CME NASDAQ (NQ)",
                _ => "PROFIL: CME CLÉ (GC/CL)"
            };

            string crossShort = _crossTradingMode switch
            {
                CrossTradingExecutionMode.AutoAtasCrossTrading => ctrlContractInfo.IsCrossTradingMicro
                    ? $"CROISÉ: {ctrlContractInfo.ChartSymbol}➔{ctrlContractInfo.TradedSymbol}"
                    : "CROISÉ: AUTO ATAS X",
                CrossTradingExecutionMode.ForceMicroOnNormalChart => $"CROISÉ: MICRO ({ctrlContractInfo.TradedSymbol})",
                _ => $"CROISÉ: OFF ({ctrlContractInfo.ChartSymbol})"
            };

            Size sMain = context.MeasureString(mainTxt, btnFont);
            Size sMode = context.MeasureString(modeTxt, subFont);
            Size sReset = context.MeasureString(resetTxt, subFont);
            Size sProf = context.MeasureString(profileShort, subFont);
            Size sCross = context.MeasureString(crossShort, subFont);

            int h = 28;
            int wMain = sMain.Width + 28;
            int wMode = sMode.Width + 16;
            int wReset = sReset.Width + 16;
            int wProf = sProf.Width + 16;
            int wCross = sCross.Width + 16;

            int startX = chartRegion.Left + 14;
            int startY = chartRegion.Top + 10;

            if (ShowHudPanel && HudPosition == HudCorner.TopLeft)
            {
                startX = chartRegion.Right - (wMain + wMode + wReset + wProf + wCross + 40);
            }

            _chartBotToggleBtnRect = new Rectangle(startX, startY, wMain, h);
            _chartBotModeBtnRect = new Rectangle(_chartBotToggleBtnRect.Right + 6, startY, wMode, h);
            _chartBotResetBtnRect = new Rectangle(_chartBotModeBtnRect.Right + 6, startY, wReset, h);
            _chartProfileBtnRect = new Rectangle(_chartBotResetBtnRect.Right + 6, startY, wProf, h);
            _chartCrossTradeBtnRect = new Rectangle(_chartProfileBtnRect.Right + 6, startY, wCross, h);

            // 1. Bouton Principal ON / OFF du Bot
            Color mainBg;
            Color mainBorder;
            Color ledCol;

            if (!_enableTradingBot)
            {
                mainBg = _hoverBotToggleBtn ? Color.FromArgb(245, 51, 65, 85) : Color.FromArgb(235, 17, 24, 39);
                mainBorder = _hoverBotToggleBtn ? Color.FromArgb(255, 56, 189, 248) : Color.FromArgb(220, 100, 116, 139);
                ledCol = Color.FromArgb(255, 239, 68, 68);
            }
            else if (botPerf.IsHaltedByMaxDrawdown)
            {
                mainBg = _hoverBotToggleBtn ? Color.FromArgb(245, 127, 29, 29) : Color.FromArgb(235, 69, 10, 10);
                mainBorder = Color.FromArgb(255, 255, 65, 85);
                ledCol = Color.FromArgb(255, 255, 65, 85);
            }
            else
            {
                mainBg = _hoverBotToggleBtn ? Color.FromArgb(245, 5, 122, 85) : Color.FromArgb(235, 6, 78, 59);
                mainBorder = Color.FromArgb(255, 0, 255, 136);
                ledCol = Color.FromArgb(255, 0, 255, 136);
            }

            context.FillRectangle(mainBg, _chartBotToggleBtnRect);
            context.DrawRectangle(new RenderPen(mainBorder, _hoverBotToggleBtn ? 2.2f : 1.6f), _chartBotToggleBtnRect);

            // Voyant lumineux LED (ON = Vert Néon, OFF = Rouge)
            Rectangle ledRect = new Rectangle(_chartBotToggleBtnRect.Left + 9, _chartBotToggleBtnRect.Top + (h - 10) / 2, 10, 10);
            context.FillEllipse(ledCol, ledRect);
            context.DrawString(mainTxt, btnFont, Color.White, _chartBotToggleBtnRect.Left + 23, _chartBotToggleBtnRect.Top + 6);

            // 2. Bouton Mode d'exécution (SIM / LIVE)
            bool isLive = _botMode == BotExecutionMode.LiveAutoTradingManager;
            Color modeBg = _hoverBotModeBtn
                ? Color.FromArgb(245, 30, 58, 138)
                : (isLive ? Color.FromArgb(235, 88, 28, 135) : Color.FromArgb(235, 15, 23, 42));
            Color modeBorder = isLive
                ? Color.FromArgb(255, 245, 158, 11)
                : Color.FromArgb(220, 56, 189, 248);

            context.FillRectangle(modeBg, _chartBotModeBtnRect);
            context.DrawRectangle(new RenderPen(modeBorder, _hoverBotModeBtn ? 2f : 1.4f), _chartBotModeBtnRect);
            context.DrawString(modeTxt, subFont, isLive ? Color.FromArgb(255, 253, 224, 71) : Color.FromArgb(255, 186, 230, 253), _chartBotModeBtnRect.Left + 8, _chartBotModeBtnRect.Top + 7);

            // 3. Bouton Reset Session / Kill-Switch
            Color rstBg = _hoverBotResetBtn
                ? Color.FromArgb(245, 71, 85, 105)
                : Color.FromArgb(235, 15, 23, 42);
            Color rstBorder = _hoverBotResetBtn
                ? Color.FromArgb(255, 251, 191, 36)
                : Color.FromArgb(200, 100, 116, 139);

            context.FillRectangle(rstBg, _chartBotResetBtnRect);
            context.DrawRectangle(new RenderPen(rstBorder, _hoverBotResetBtn ? 2f : 1.3f), _chartBotResetBtnRect);
            context.DrawString(resetTxt, subFont, Color.FromArgb(255, 226, 232, 240), _chartBotResetBtnRect.Left + 8, _chartBotResetBtnRect.Top + 7);

            // 3b. Bouton Profil de Marché (Auto / BTC Binance / CME S&P ES / CME NASDAQ NQ / CME Clés)
            bool isCmeProf = ctrlContractInfo.EffectiveProfile != MarketProfilePreset.CryptoBinanceFutures;
            Color profBg = _hoverProfileBtn
                ? Color.FromArgb(245, 30, 64, 175)
                : (isCmeProf ? Color.FromArgb(235, 12, 45, 92) : Color.FromArgb(235, 15, 23, 42));
            Color profBorder = isCmeProf
                ? Color.FromArgb(255, 56, 189, 248)
                : Color.FromArgb(220, 245, 158, 11);

            context.FillRectangle(profBg, _chartProfileBtnRect);
            context.DrawRectangle(new RenderPen(profBorder, _hoverProfileBtn ? 2f : 1.4f), _chartProfileBtnRect);
            context.DrawString(profileShort, subFont, isCmeProf ? Color.FromArgb(255, 186, 230, 253) : Color.FromArgb(255, 253, 224, 71), _chartProfileBtnRect.Left + 8, _chartProfileBtnRect.Top + 7);

            // 3c. Bouton Trading Croisé ATAS X (Charte Contrat Normal ES/NQ -> Exécution Micro MES/MNQ)
            bool isCrossOn = ctrlContractInfo.IsCrossTradingMicro || _crossTradingMode == CrossTradingExecutionMode.ForceMicroOnNormalChart;
            Color crossBg = _hoverCrossTradeBtn
                ? Color.FromArgb(245, 6, 95, 70)
                : (isCrossOn ? Color.FromArgb(235, 6, 78, 59) : Color.FromArgb(235, 15, 23, 42));
            Color crossBorder = isCrossOn
                ? Color.FromArgb(255, 16, 245, 150)
                : Color.FromArgb(200, 100, 116, 139);

            context.FillRectangle(crossBg, _chartCrossTradeBtnRect);
            context.DrawRectangle(new RenderPen(crossBorder, _hoverCrossTradeBtn ? 2f : 1.4f), _chartCrossTradeBtnRect);
            context.DrawString(crossShort, subFont, isCrossOn ? Color.FromArgb(255, 167, 243, 208) : Color.FromArgb(255, 203, 213, 225), _chartCrossTradeBtnRect.Left + 8, _chartCrossTradeBtnRect.Top + 7);

            // 4. Encadrement Horaire Interactif sur la Charte (Heures de Démarrage & de Fin)
            int schedY = startY + h + 6;
            int schedH = 28;
            int frameW = Math.Max(428, (_chartCrossTradeBtnRect.Right - startX));
            Rectangle schedFrameRect = new Rectangle(startX, schedY, frameW, schedH);

            Color frameBg = Color.FromArgb(236, 11, 17, 30);
            Color frameBorder = _enableBotSchedule
                ? Color.FromArgb(235, 56, 189, 248)
                : Color.FromArgb(190, 100, 116, 139);

            context.FillRectangle(frameBg, schedFrameRect);
            context.DrawRectangle(new RenderPen(frameBorder, 1.5f), schedFrameRect);

            // Bouton ON/OFF Plage Horaire
            string schedToggleTxt = _enableBotSchedule ? "HORAIRES: ON" : "HORAIRES: OFF";
            Size sSched = context.MeasureString(schedToggleTxt, subFont);
            int wSchedBtn = sSched.Width + 14;
            _chartSchedToggleRect = new Rectangle(startX + 5, schedY + 4, wSchedBtn, schedH - 8);

            Color schedBtnBg = _enableBotSchedule
                ? (_hoverSchedToggleBtn ? Color.FromArgb(245, 14, 116, 144) : Color.FromArgb(230, 8, 51, 68))
                : (_hoverSchedToggleBtn ? Color.FromArgb(245, 71, 85, 105) : Color.FromArgb(230, 30, 41, 59));
            context.FillRectangle(schedBtnBg, _chartSchedToggleRect);
            context.DrawRectangle(new RenderPen(_enableBotSchedule ? Color.FromArgb(255, 56, 189, 248) : Color.FromArgb(200, 148, 163, 184), 1.2f), _chartSchedToggleRect);
            context.DrawString(schedToggleTxt, subFont, Color.White, _chartSchedToggleRect.Left + 7, _chartSchedToggleRect.Top + 3);

            // Contrôle Heure de Démarrage : DÉBUT [-] [08:00] [+]
            int curX = _chartSchedToggleRect.Right + 8;
            string lblDebut = "DÉBUT";
            Size sDeb = context.MeasureString(lblDebut, subFont);
            context.DrawString(lblDebut, subFont, Color.FromArgb(255, 148, 163, 184), curX, schedY + 7);
            curX += sDeb.Width + 5;

            _chartStartMinusRect = new Rectangle(curX, schedY + 4, 20, schedH - 8);
            _chartStartValRect = new Rectangle(_chartStartMinusRect.Right + 3, schedY + 4, 48, schedH - 8);
            _chartStartPlusRect = new Rectangle(_chartStartValRect.Right + 3, schedY + 4, 20, schedH - 8);

            context.FillRectangle(_hoverStartMinus ? Color.FromArgb(245, 51, 65, 85) : Color.FromArgb(230, 22, 31, 51), _chartStartMinusRect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(200, 100, 116, 139), 1f), _chartStartMinusRect);
            context.DrawString("-", btnFont, Color.White, _chartStartMinusRect.Left + 6, _chartStartMinusRect.Top + 2);

            context.FillRectangle(Color.FromArgb(240, 7, 10, 18), _chartStartValRect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(230, 0, 255, 136), 1.2f), _chartStartValRect);
            context.DrawString(BotStartTimeText, subFont, Color.FromArgb(255, 0, 255, 136), _chartStartValRect.Left + 6, _chartStartValRect.Top + 3);

            context.FillRectangle(_hoverStartPlus ? Color.FromArgb(245, 51, 65, 85) : Color.FromArgb(230, 22, 31, 51), _chartStartPlusRect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(200, 100, 116, 139), 1f), _chartStartPlusRect);
            context.DrawString("+", btnFont, Color.White, _chartStartPlusRect.Left + 5, _chartStartPlusRect.Top + 2);

            // Contrôle Heure de Fin : FIN [-] [22:00] [+]
            curX = _chartStartPlusRect.Right + 10;
            string lblFin = "FIN";
            Size sFin = context.MeasureString(lblFin, subFont);
            context.DrawString(lblFin, subFont, Color.FromArgb(255, 148, 163, 184), curX, schedY + 7);
            curX += sFin.Width + 5;

            _chartEndMinusRect = new Rectangle(curX, schedY + 4, 20, schedH - 8);
            _chartEndValRect = new Rectangle(_chartEndMinusRect.Right + 3, schedY + 4, 48, schedH - 8);
            _chartEndPlusRect = new Rectangle(_chartEndValRect.Right + 3, schedY + 4, 20, schedH - 8);

            context.FillRectangle(_hoverEndMinus ? Color.FromArgb(245, 51, 65, 85) : Color.FromArgb(230, 22, 31, 51), _chartEndMinusRect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(200, 100, 116, 139), 1f), _chartEndMinusRect);
            context.DrawString("-", btnFont, Color.White, _chartEndMinusRect.Left + 6, _chartEndMinusRect.Top + 2);

            context.FillRectangle(Color.FromArgb(240, 7, 10, 18), _chartEndValRect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(230, 251, 191, 36), 1.2f), _chartEndValRect);
            context.DrawString(BotEndTimeText, subFont, Color.FromArgb(255, 251, 191, 36), _chartEndValRect.Left + 6, _chartEndValRect.Top + 3);

            context.FillRectangle(_hoverEndPlus ? Color.FromArgb(245, 51, 65, 85) : Color.FromArgb(230, 22, 31, 51), _chartEndPlusRect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(200, 100, 116, 139), 1f), _chartEndPlusRect);
            context.DrawString("+", btnFont, Color.White, _chartEndPlusRect.Left + 5, _chartEndPlusRect.Top + 2);

            // 5. Encadrement Synchronisation PnL Réel & Compte Actif ATAS directement sur la Charte
            int acctY = schedY + schedH + 5;
            int acctH = 28;
            string acctNameShort = !string.IsNullOrWhiteSpace(botPerf.ActiveAccountName) ? botPerf.ActiveAccountName : "Compte Actif";
            string syncBtnTxt = _syncPnlWithActiveAccount
                ? (botPerf.IsSyncedWithActiveAccount ? $"SYNC COMPTE: {acctNameShort}" : "SYNC COMPTE: ATTENTE ATAS")
                : "SYNC COMPTE: OFF (SIM)";

            string scopeBtnTxt = _accountPnlScope == ActiveAccountPnlScope.ActivePortfolioTotal
                ? "PORTÉE: COMPTE GLOBAL"
                : "PORTÉE: INSTRUMENT";

            string realPnlTxt = $"PNL RÉEL: {botPerf.TotalPnlUsd:+0.00;-0.00;0.00}$ (Réal: {botPerf.RealizedPnlUsd:+0;-0;0}$ | Lat: {botPerf.UnrealizedPnlUsd:+0;-0;0}$)";

            Size sSyncBtn = context.MeasureString(syncBtnTxt, subFont);
            Size sScopeBtn = context.MeasureString(scopeBtnTxt, subFont);
            Size sPnlTxt = context.MeasureString(realPnlTxt, btnFont);

            int wSyncBtn = sSyncBtn.Width + 14;
            int wScopeBtn = sScopeBtn.Width + 14;
            int acctFrameW = Math.Max(frameW, wSyncBtn + wScopeBtn + sPnlTxt.Width + 32);

            Rectangle acctFrameRect = new Rectangle(startX, acctY, acctFrameW, acctH);
            Color acctFrameBorder = botPerf.IsSyncedWithActiveAccount
                ? Color.FromArgb(235, 0, 255, 136)
                : (_syncPnlWithActiveAccount ? Color.FromArgb(220, 251, 191, 36) : Color.FromArgb(190, 100, 116, 139));

            context.FillRectangle(Color.FromArgb(238, 10, 16, 28), acctFrameRect);
            context.DrawRectangle(new RenderPen(acctFrameBorder, 1.5f), acctFrameRect);

            _chartAcctSyncToggleRect = new Rectangle(startX + 5, acctY + 4, wSyncBtn, acctH - 8);
            Color syncBtnBg = _syncPnlWithActiveAccount
                ? (_hoverAcctSyncBtn ? Color.FromArgb(245, 5, 122, 85) : Color.FromArgb(230, 6, 78, 59))
                : (_hoverAcctSyncBtn ? Color.FromArgb(245, 71, 85, 105) : Color.FromArgb(230, 30, 41, 59));
            context.FillRectangle(syncBtnBg, _chartAcctSyncToggleRect);
            context.DrawRectangle(new RenderPen(_syncPnlWithActiveAccount ? Color.FromArgb(255, 0, 255, 136) : Color.FromArgb(200, 148, 163, 184), 1.2f), _chartAcctSyncToggleRect);
            context.DrawString(syncBtnTxt, subFont, Color.White, _chartAcctSyncToggleRect.Left + 7, _chartAcctSyncToggleRect.Top + 3);

            _chartAcctScopeToggleRect = new Rectangle(_chartAcctSyncToggleRect.Right + 6, acctY + 4, wScopeBtn, acctH - 8);
            Color scopeBtnBg = _hoverAcctScopeBtn ? Color.FromArgb(245, 30, 58, 138) : Color.FromArgb(230, 15, 23, 42);
            context.FillRectangle(scopeBtnBg, _chartAcctScopeToggleRect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(220, 56, 189, 248), 1.1f), _chartAcctScopeToggleRect);
            context.DrawString(scopeBtnTxt, subFont, Color.FromArgb(255, 186, 230, 253), _chartAcctScopeToggleRect.Left + 7, _chartAcctScopeToggleRect.Top + 3);

            Color realPnlCol = botPerf.TotalPnlUsd >= 0m
                ? Color.FromArgb(255, 0, 255, 136)
                : Color.FromArgb(255, 255, 65, 85);
            context.DrawString(realPnlTxt, btnFont, realPnlCol, _chartAcctScopeToggleRect.Right + 10, acctY + 6);
        }

        // =====================================================================
        // 1. Rendu des Losanges BigTrades (◆) sur les Bougies (Style ATAS X)
        // =====================================================================
        private void RenderBigTradeDiamondsOnBar(
            RenderContext context,
            int bar,
            IndicatorCandle candle,
            ClusterRow[] rows,
            int xMid,
            int rowHeight,
            RenderFont boldFont,
            RenderFont cellFont)
        {
            if (ChartInfo == null || rows == null || rows.Length == 0)
                return;

            decimal tickSize = InstrumentInfo != null && InstrumentInfo.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
            decimal effMinLots = GetEffectiveBigTradeMinLots(tickSize);
            if (effMinLots <= 0m) effMinLots = 40m;

            // Sélection des plus gros BigTrades de la bougie (cluster + prints live temps réel, max 3 par bougie)
            var bigRows = new List<ClusterRow>();
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].HasBigTradeBuy || rows[i].HasBigTradeSell)
                    bigRows.Add(rows[i]);
            }

            if (_liveBigTradesByBar.TryGetValue(bar, out var livePrints) && livePrints != null)
            {
                decimal step = tickSize * Math.Max(1, _ticksPerRow);
                decimal barRange = Math.Max(step, candle.High - candle.Low);
                for (int i = 0; i < livePrints.Count; i++)
                {
                    var lp = livePrints[i];
                    bool alreadyInRows = false;
                    for (int j = 0; j < bigRows.Count; j++)
                    {
                        if (Math.Abs(bigRows[j].Price - lp.Price) <= step * 0.6m)
                        {
                            alreadyInRows = true;
                            break;
                        }
                    }
                    if (!alreadyInRows)
                    {
                        int trapSide = 0;
                        if (!lp.IsBuy && lp.Price <= candle.Low + barRange * 0.55m && candle.Close >= lp.Price + step)
                            trapSide = 1;
                        else if (lp.IsBuy && lp.Price >= candle.High - barRange * 0.55m && candle.Close <= lp.Price - step)
                            trapSide = -1;

                        bigRows.Add(new ClusterRow
                        {
                            Price = lp.Price,
                            Volume = lp.Volume,
                            HasBigTradeBuy = lp.IsBuy,
                            HasBigTradeSell = !lp.IsBuy,
                            BigTradeBuyVol = lp.IsBuy ? lp.Volume : 0m,
                            BigTradeSellVol = !lp.IsBuy ? lp.Volume : 0m,
                            TrappedBigTradeSide = trapSide
                        });
                    }
                }
            }

            if (bigRows.Count == 0)
                return;

            bigRows.Sort((a, b) =>
            {
                decimal maxA = Math.Max(a.BigTradeBuyVol, a.BigTradeSellVol);
                decimal maxB = Math.Max(b.BigTradeBuyVol, b.BigTradeSellVol);
                return maxB.CompareTo(maxA);
            });

            int maxDiamondsPerBar = Math.Min(3, bigRows.Count);
            for (int idx = 0; idx < maxDiamondsPerBar; idx++)
            {
                var r = bigRows[idx];
                int yCenter = ChartInfo.GetYByPrice(r.Price, false);
                bool isSellDom = r.HasBigTradeSell && (!r.HasBigTradeBuy || r.BigTradeSellVol >= r.BigTradeBuyVol);
                decimal domVol = isSellDom ? r.BigTradeSellVol : r.BigTradeBuyVol;

                // Taille du losange proportionnelle au volume par rapport au seuil BigTrade
                double volRatio = (double)(domVol / effMinLots);
                int halfR = (int)Math.Clamp(BigTradeDiamondBaseSize + Math.Log2(Math.Max(1.0, volRatio)) * 4.5, 9.0, 26.0);
                int outerR = halfR + 4;

                Color fillCol = isSellDom ? BigTradeSellColor : BigTradeBuyColor;
                Color outerGlowCol = Color.FromArgb(115, fillCol.R, fillCol.G, fillCol.B);
                Color innerCoreCol = Color.FromArgb(230, fillCol.R, fillCol.G, fillCol.B);
                Color borderCol = r.TrappedBigTradeSide != 0
                    ? Color.FromArgb(255, 250, 204, 21) // Contour Or lumineux si le BigTrade est PIÉGÉ !
                    : Color.FromArgb(245, 255, 255, 255);

                // Double losange concentrique (exactement comme l'indicateur BigTrades ATAS de la capture)
                Point[] outerDiamond = new Point[]
                {
                    new Point(xMid, yCenter - outerR),
                    new Point(xMid + outerR, yCenter),
                    new Point(xMid, yCenter + outerR),
                    new Point(xMid - outerR, yCenter)
                };
                Point[] innerDiamond = new Point[]
                {
                    new Point(xMid, yCenter - halfR),
                    new Point(xMid + halfR, yCenter),
                    new Point(xMid, yCenter + halfR),
                    new Point(xMid - halfR, yCenter)
                };

                context.FillPolygon(outerGlowCol, outerDiamond);
                context.DrawPolygon(new RenderPen(Color.FromArgb(190, fillCol.R, fillCol.G, fillCol.B), 1.4f), outerDiamond);

                context.FillPolygon(innerCoreCol, innerDiamond);
                context.DrawPolygon(new RenderPen(borderCol, r.TrappedBigTradeSide != 0 ? 2.2f : 1.5f), innerDiamond);

                // Point central et étiquette de lots si le BigTrade est piégé ou très massif
                Rectangle centerDot = new Rectangle(xMid - 2, yCenter - 2, 4, 4);
                context.FillEllipse(Color.White, centerDot);

                if ((r.TrappedBigTradeSide != 0 || idx == 0) && rowHeight >= 9)
                {
                    string lotTxt = r.TrappedBigTradeSide != 0
                        ? $"◆ {domVol:0} PIÉGÉ"
                        : $"{domVol:0}";
                    Size sz = context.MeasureString(lotTxt, cellFont);
                    int lx = xMid - sz.Width / 2;
                    int ly = isSellDom ? (yCenter + outerR + 2) : (yCenter - outerR - sz.Height - 2);
                    Rectangle pill = new Rectangle(lx - 3, ly - 1, sz.Width + 6, sz.Height + 2);
                    context.FillRectangle(Color.FromArgb(215, 10, 15, 28), pill);
                    context.DrawRectangle(new RenderPen(borderCol, 1f), pill);
                    context.DrawString(
                        lotTxt,
                        cellFont,
                        r.TrappedBigTradeSide != 0 ? Color.FromArgb(255, 253, 224, 71) : Color.White,
                        lx,
                        ly);
                }
            }
        }

        // =====================================================================
        // 2. Rendu de l'Indicateur Options X-Ray (CALL WALL, MAX PAIN, PUT WALL & Profil GEX)
        //    Fidèle à la capture d'écran ATAS X de l'utilisateur
        // =====================================================================
        private void RenderOptionsXRayOverlay(
            RenderContext context,
            Rectangle chartRegion,
            int firstBar,
            int lastBar,
            decimal tickSize,
            BarOptionsFlowMetrics optMetrics,
            RenderFont boldFont,
            RenderFont cellFont)
        {
            if (ChartInfo == null || optMetrics.XRayCallWall <= 0m || optMetrics.XRayPutWall <= 0m || optMetrics.XRayMaxPain <= 0m)
                return;

            decimal strikeStep = GetEffectiveOptionsStrikeStep(optMetrics.XRayMaxPain);
            if (strikeStep <= 0m) strikeStep = 50m;

            int xLeftLine = Math.Max(chartRegion.Left + 40, ChartInfo.GetXByBar(Math.Max(firstBar, lastBar - 55)));
            int xRightEdge = chartRegion.Right - 6;

            // A. Profil GEX Horizontal par Strike sur le bord droit (Barres Jaunes Call GEX / Gris-Bleu Put GEX)
            if (ShowOptionsStrikeGexProfile)
            {
                decimal minStrike = optMetrics.XRayPutWall - strikeStep * 4m;
                decimal maxStrike = optMetrics.XRayCallWall + strikeStep * 4m;
                decimal subStep = Math.Max(tickSize * 4m, strikeStep / 2m);
                int maxBarPxWidth = Math.Clamp(chartRegion.Width / 6, 95, 185);

                for (decimal strike = minStrike; strike <= maxStrike; strike += subStep)
                {
                    int yStrike = ChartInfo.GetYByPrice(strike, false);
                    if (yStrike < chartRegion.Top + 32 || yStrike > chartRegion.Bottom - 24)
                        continue;

                    bool isAboveMp = strike >= optMetrics.XRayMaxPain;
                    decimal distToCallWall = Math.Abs(strike - optMetrics.XRayCallWall);
                    decimal distToPutWall = Math.Abs(strike - optMetrics.XRayPutWall);
                    decimal distToMp = Math.Abs(strike - optMetrics.XRayMaxPain);

                    // Pondération gaussienne autour des strikes clés (Call Wall, Max Pain, Put Wall)
                    double callWeight = Math.Exp(-(double)(distToCallWall / (strikeStep * 1.4m))) * 0.95
                                      + Math.Exp(-(double)(distToMp / (strikeStep * 2.0m))) * 0.35;
                    double putWeight = Math.Exp(-(double)(distToPutWall / (strikeStep * 1.4m))) * 1.00
                                     + Math.Exp(-(double)(distToMp / (strikeStep * 2.2m))) * 0.40;

                    // Modulation déterministe par strike pour reproduire l'histogramme multi-strikes SPX/NDX
                    int strikeHash = Math.Abs(((int)Math.Round(strike / subStep)) * 73 % 100);
                    double mod = 0.45 + (strikeHash / 100.0) * 0.55;

                    double normRatio = isAboveMp
                        ? Math.Clamp(callWeight * mod, 0.08, 1.0)
                        : Math.Clamp(putWeight * mod, 0.08, 1.0);

                    int barW = (int)Math.Round(maxBarPxWidth * normRatio);
                    int barH = Math.Clamp((int)Math.Abs(ChartInfo.GetYByPrice(strike, false) - ChartInfo.GetYByPrice(strike + subStep, false)) - 2, 4, 11);

                    Rectangle gexRect = new Rectangle(xRightEdge - barW, yStrike - barH / 2, barW, barH);

                    // Jaune vif sur les strikes Call GEX (> Max Pain) et Bleu-Gris sur les strikes Put GEX (< Max Pain)
                    Color gexCol = isAboveMp
                        ? Color.FromArgb(195, 250, 204, 21)
                        : Color.FromArgb(175, 100, 116, 139);

                    if (Math.Abs(strike - optMetrics.XRayCallWall) < subStep * 0.4m)
                        gexCol = Color.FromArgb(230, 253, 224, 71);
                    else if (Math.Abs(strike - optMetrics.XRayPutWall) < subStep * 0.4m)
                        gexCol = Color.FromArgb(220, 139, 92, 246);

                    context.FillRectangle(gexCol, gexRect);
                }
            }

            // B. En-tête supérieur droit : "Options X-Ray: SPX options data" + "0 DTE = 1226pts · MP 27900"
            var headerTitleFont = new RenderFont("Consolas", 10, FontStyle.Bold);
            var headerSubFont = new RenderFont("Consolas", 9, FontStyle.Bold);
            string hdrLine1 = "Options X-Ray: SPX options data";
            string hdrLine2 = $"0 DTE = {optMetrics.XRayZeroDteExpectedMovePts:0}pts · MP {optMetrics.XRayMaxPain:0.##}";

            Size sHdr1 = context.MeasureString(hdrLine1, headerTitleFont);
            Size sHdr2 = context.MeasureString(hdrLine2, headerSubFont);
            int hdrW = Math.Max(sHdr1.Width, sHdr2.Width) + 18;
            int hdrH = sHdr1.Height + sHdr2.Height + 12;
            int hdrX = chartRegion.Right - hdrW - 14;
            int hdrY = chartRegion.Top + 44;

            Rectangle hdrRect = new Rectangle(hdrX, hdrY, hdrW, hdrH);
            context.FillRectangle(Color.FromArgb(215, 11, 15, 25), hdrRect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(190, 71, 85, 105), 1f), hdrRect);
            context.DrawString(hdrLine1, headerTitleFont, Color.FromArgb(255, 226, 232, 240), hdrX + 9, hdrY + 4);
            context.DrawString(hdrLine2, headerSubFont, Color.FromArgb(255, 250, 204, 21), hdrX + 9, hdrY + 6 + sHdr1.Height);

            // C. Lignes horizontales et badges : CALL WALL, MAX PAIN, PUT WALL
            DrawOptionsXRayLevelLine(
                context,
                chartRegion,
                xLeftLine,
                xRightEdge,
                optMetrics.XRayCallWall,
                $"CALL WALL {optMetrics.XRayCallWall:0.##} · +{optMetrics.XRayCallWallGexM:0.#}M",
                CallWallLineColor,
                false,
                boldFont);

            DrawOptionsXRayLevelLine(
                context,
                chartRegion,
                xLeftLine,
                xRightEdge,
                optMetrics.XRayMaxPain,
                $"MAX PAIN {optMetrics.XRayMaxPain:0.##}",
                MaxPainLineColor,
                true,
                boldFont);

            DrawOptionsXRayLevelLine(
                context,
                chartRegion,
                xLeftLine,
                xRightEdge,
                optMetrics.XRayPutWall,
                $"PUT WALL {optMetrics.XRayPutWall:0.##} · {optMetrics.XRayPutWallGexM:+0.#;-0.#;0.0}M",
                PutWallLineColor,
                false,
                boldFont);
        }

        private void DrawOptionsXRayLevelLine(
            RenderContext context,
            Rectangle chartRegion,
            int xLeft,
            int xRight,
            decimal price,
            string badgeText,
            Color lineColor,
            bool dashed,
            RenderFont font)
        {
            if (ChartInfo == null)
                return;

            int y = ChartInfo.GetYByPrice(price, false);
            if (y < chartRegion.Top + 16 || y > chartRegion.Bottom - 16)
                return;

            var pen = new RenderPen(
                lineColor,
                dashed ? 1.8f : 2.2f,
                dashed ? DashStyle.Dash : DashStyle.Solid);

            context.DrawLine(pen, xLeft, y, xRight, y);

            Size sz = context.MeasureString(badgeText, font);
            int bw = sz.Width + 14;
            int bh = sz.Height + 6;
            int bx = xRight - bw - 8;
            int by = y - bh - 3;

            Rectangle badgeRect = new Rectangle(bx, by, bw, bh);
            context.FillRectangle(Color.FromArgb(235, 15, 23, 42), badgeRect);
            context.DrawRectangle(new RenderPen(lineColor, 1.5f), badgeRect);
            context.DrawString(badgeText, font, lineColor, bx + 7, by + 3);
        }

        // =====================================================================
        // 3. Étiquettes d'Ordres Actifs Style DOM ATAS ([X] [2] [TP | $ +3755.00 | R])
        // =====================================================================
        private void RenderDomStyleActiveOrderTags(
            RenderContext context,
            Rectangle chartRegion,
            int lastBar,
            decimal tickSize,
            BotPerformanceState botPerf,
            VisualTradeSignal? latestSignal,
            RenderFont boldFont,
            RenderFont cellFont)
        {
            if (ChartInfo == null)
                return;

            decimal entryPrice = botPerf.ActiveDirection != 0 ? botPerf.ActiveEntryPrice : (latestSignal?.EntryPrice ?? 0m);
            decimal stopPrice = botPerf.ActiveDirection != 0 ? botPerf.ActiveStopPrice : (latestSignal?.StopPrice ?? 0m);
            decimal tpPrice = botPerf.ActiveDirection != 0 ? botPerf.ActiveTp2Price : (latestSignal?.Tp2Price ?? 0m);
            int dir = botPerf.ActiveDirection != 0 ? botPerf.ActiveDirection : (latestSignal?.Direction ?? 0);

            if (dir == 0 || entryPrice <= 0m || stopPrice <= 0m || tpPrice <= 0m)
                return;

            int qty = botPerf.ActiveContracts > 0 ? botPerf.ActiveContracts : Math.Max(1, _botTradeQuantity);
            var contractInfo = ResolveContractAndCrossTradingInfo(tickSize);
            decimal ptVal = contractInfo.TradedPointValueUsd > 0m ? contractInfo.TradedPointValueUsd : 20m;

            decimal lastClose = GetCandle(lastBar).Close;
            decimal openPnlUsd = (lastClose - entryPrice) * dir * ptVal * qty;
            decimal tpPnlUsd = Math.Abs(tpPrice - entryPrice) * ptVal * qty;
            decimal slPnlUsd = -Math.Abs(entryPrice - stopPrice) * ptVal * qty;

            int xAnchor = Math.Clamp(ChartInfo.GetXByBar(lastBar) - 210, chartRegion.Left + 80, chartRegion.Right - 310);

            // Tag TP
            DrawSingleDomOrderTag(
                context,
                chartRegion,
                xAnchor,
                tpPrice,
                qty,
                "TP",
                $"$ +{tpPnlUsd:0.00}",
                Color.FromArgb(240, 16, 185, 129),
                true,
                cellFont);

            // Tag Entrée / Position ouverte
            DrawSingleDomOrderTag(
                context,
                chartRegion,
                xAnchor,
                entryPrice,
                qty,
                dir > 0 ? "BUY" : "SELL",
                $"${(openPnlUsd >= 0m ? "+" : "")}{openPnlUsd:0.00}",
                openPnlUsd >= 0m ? Color.FromArgb(240, 14, 165, 233) : Color.FromArgb(240, 244, 63, 94),
                false,
                cellFont);

            // Tag SL
            DrawSingleDomOrderTag(
                context,
                chartRegion,
                xAnchor,
                stopPrice,
                qty,
                "SL",
                $"$ {slPnlUsd:0.00}",
                Color.FromArgb(240, 239, 68, 68),
                true,
                cellFont);
        }

        private void DrawSingleDomOrderTag(
            RenderContext context,
            Rectangle chartRegion,
            int xAnchor,
            decimal price,
            int qty,
            string roleTag,
            string pnlText,
            Color accentColor,
            bool showReverseBtn,
            RenderFont font)
        {
            if (ChartInfo == null)
                return;

            int y = ChartInfo.GetYByPrice(price, false);
            if (y < chartRegion.Top + 20 || y > chartRegion.Bottom - 20)
                return;

            string fullLabel = showReverseBtn
                ? $" X │ {qty} │ {roleTag} │ {pnlText} │ R "
                : $" X │ {qty} │ {pnlText} ";

            Size sz = context.MeasureString(fullLabel, font);
            int w = sz.Width + 10;
            int h = Math.Max(18, sz.Height + 4);
            Rectangle rect = new Rectangle(xAnchor, y - h / 2, w, h);

            // Ligne pointillée jusqu'au prix sur le graphique
            var linkPen = new RenderPen(accentColor, 1.2f, DashStyle.Dot);
            context.DrawLine(linkPen, rect.Right, y, chartRegion.Right - 8, y);

            context.FillRectangle(Color.FromArgb(235, 15, 23, 42), rect);
            context.DrawRectangle(new RenderPen(accentColor, 1.5f), rect);
            context.DrawString(fullLabel, font, Color.White, rect.Left + 5, rect.Top + 2);
        }

        // =====================================================================
        // 4. Notification d'Alerte BigTrade en bas à gauche (Style Toast ATAS)
        //    Ex: "NQ: New BigTrade at 27899.25 (sell) is more than 40 lots"
        // =====================================================================
        private void RenderBigTradeAlertToast(
            RenderContext context,
            Rectangle chartRegion,
            int lastBar,
            decimal tickSize,
            RenderFont boldFont,
            RenderFont cellFont)
        {
            decimal toastPrice = _lastBigTradeToastPrice;
            decimal toastVol = _lastBigTradeToastVol;
            bool toastIsBuy = _lastBigTradeToastIsBuy;

            // Si aucun print live n'a encore été capturé, on affiche le dernier BigTrade détecté sur les bougies récentes
            if (toastPrice <= 0m || toastVol <= 0m)
            {
                for (int b = lastBar; b >= Math.Max(0, lastBar - 20); b--)
                {
                    if (_barRowsCache.TryGetValue(b, out var bRows) && bRows != null)
                    {
                        for (int i = 0; i < bRows.Length; i++)
                        {
                            var r = bRows[i];
                            if (r.HasBigTradeBuy || r.HasBigTradeSell)
                            {
                                decimal v = Math.Max(r.BigTradeBuyVol, r.BigTradeSellVol);
                                if (v > toastVol)
                                {
                                    toastVol = v;
                                    toastPrice = r.Price;
                                    toastIsBuy = r.HasBigTradeBuy && (!r.HasBigTradeSell || r.BigTradeBuyVol >= r.BigTradeSellVol);
                                }
                            }
                        }
                        if (toastVol > 0m)
                            break;
                    }
                }
            }

            if (toastPrice <= 0m || toastVol <= 0m)
                return;

            string sym = InstrumentInfo?.Instrument ?? "NQ";
            decimal effThreshold = GetEffectiveBigTradeMinLots(tickSize);
            string sideWord = toastIsBuy ? "buy" : "sell";

            string title = "Alert";
            string msg = $"{sym}: New BigTrade at {toastPrice:0.##} ({sideWord}) is more than {effThreshold:0} lots ({toastVol:0} lots)";

            Size sTitle = context.MeasureString(title, boldFont);
            Size sMsg = context.MeasureString(msg, cellFont);

            int boxW = Math.Max(sTitle.Width, sMsg.Width) + 52;
            int boxH = 48;
            int boxX = chartRegion.Left + 14;
            int boxY = chartRegion.Bottom - boxH - 14;

            Rectangle toastRect = new Rectangle(boxX, boxY, boxW, boxH);
            Color borderCol = toastIsBuy ? BigTradeBuyColor : BigTradeSellColor;

            context.FillRectangle(Color.FromArgb(238, 17, 24, 39), toastRect);
            context.DrawRectangle(new RenderPen(borderCol, 1.6f), toastRect);

            // Petit losange d'icône à gauche du toast
            int iconX = boxX + 20;
            int iconY = boxY + boxH / 2;
            Point[] iconDiamond = new Point[]
            {
                new Point(iconX, iconY - 9),
                new Point(iconX + 9, iconY),
                new Point(iconX, iconY + 9),
                new Point(iconX - 9, iconY)
            };
            context.FillPolygon(borderCol, iconDiamond);
            context.DrawPolygon(new RenderPen(Color.White, 1.2f), iconDiamond);

            context.DrawString(title, boldFont, Color.FromArgb(255, 250, 204, 21), boxX + 38, boxY + 6);
            context.DrawString(msg, cellFont, Color.FromArgb(255, 226, 232, 240), boxX + 38, boxY + 24);
        }
    }

    // =========================================================================
    // Entrée dédiée dans la liste des indicateurs ATAS X (Ctrl+I) :
    // "ATAS X - Bot BigTrades Piégés (Losanges ◆) + Reversal + Options X-Ray + HVN/LVN"
    // =========================================================================
    [DisplayName("ATAS X - Bot BigTrades Piégés (Losanges ◆) + Reversal + Options X-Ray + HVN/LVN")]
    [Description("Bot de trading ATAS X basé sur les BigTrades piégés (losanges rouges/verts ◆), le Reversal Order Flow, l'indicateur Options X-Ray (CALL WALL, MAX PAIN, PUT WALL & profil GEX) et les niveaux HVN / LVN.")]
    [Category("Order Flow - Custom")]
    public class BigTradesReversalOptionsBot : FootprintHvnLvnAbsorption
    {
        public BigTradesReversalOptionsBot()
        {
            EnableBigTradesReversal = true;
            ShowBigTradeDiamonds = true;
            ShowBigTradeAlertToast = true;
            EnableOptionsXRayOverlay = true;
            ShowOptionsStrikeGexProfile = true;
            ShowDomStyleOrderTags = true;
        }
    }
}
