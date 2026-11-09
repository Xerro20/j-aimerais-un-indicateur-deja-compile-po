// =============================================================================
//  Footprint HVN / LVN, Heavy Absorption, Trapped Traders & Options Flow Alerts
//  Cible : .NET 10.0 (ATAS X Windows & macOS)
//
//  Fonctionnalités principales :
//    1. HVN (High Volume Nodes) :
//       - Détection intra-bougie sur le Footprint (POC + clusters à fort volume
//         >= % du POC et pic local de volume).
//       - Profil de Volume Composite (Session ou Rolling N bougies) avec zones
//         HVN projetées horizontalement sur le graphique.
//    2. LVN (Low Volume Nodes) :
//       - Détection intra-bougie des creux de liquidité (poches de faible volume /
//         single prints situés entre deux zones plus denses).
//       - Détection des vallées LVN sur le Profil de Volume Composite avec
//         projection horizontale des niveaux de rejet rapide.
//    3. Heavy Absorption (Absorption Massive Bid / Ask sur le Footprint) :
//       - Filtrage multi-critères : Volume absolu minimum, Multiplicateur de
//         volume par rapport à la moyenne des niveaux de la bougie, Dominance
//         Bid/Ask (%), Ratio d'Imbalance diagonale, Localisation (extrêmes /
//         mèches ou mur intra-bar) et Rejet du prix (clôture opposée).
//       - Surbrillance néon directe sur la cellule Footprint absorbée, badge
//         "ABS ▲ / ▼" avec volume & delta, marqueur sur l'extrême de la bougie
//         et rayon S/R projeté jusqu'à mitigation.
//    4. Acheteurs & Vendeurs Piégés (Trapped Buyers & Trapped Sellers) :
//       - Acheteurs Piégés (Trapped Buyers ▼ - Baissier) : Agressivité acheteuse
//         forte (Ask >> Bid, imbalance diagonale acheteuse, delta positif) dans
//         la mèche haute / extrême supérieur, suivie d'un rejet baissier (la
//         bougie clôture nettement sous le cluster d'acheteurs piégés).
//       - Vendeurs Piégés (Trapped Sellers ▲ - Haussier) : Agressivité vendeuse
//         forte (Bid >> Ask, imbalance diagonale vendeuse, delta négatif) dans
//         la mèche basse / extrême inférieur, suivie d'un rejet haussier (la
//         bougie clôture nettement au-dessus du cluster de vendeurs piégés).
//    5. Alerte Visuelle Achat / Vente & Flux d'Options ATAS X :
//       - Intègre le flux d'options fourni par ATAS X via IOptionsDataFeed /
//         Security (OptionTypes.Call / Put, StrikePrice, OpenInterest), les
//         variations d'Open Interest (ΔOI = Candle.OI), les Sweeps / Block Trades
//         (OnNewTrade / OnCumulativeTrade) et l'asymétrie Call Flow vs Put Flow.
//       - Déclenche une Alerte Visuelle ACHAT ▲ (BUY) ou VENTE ▼ (SELL) sur le
//         graphique (cartouche sur la bougie + bannière visuelle d'alerte +
//         niveaux Entrée / Stop-Loss + alertes sonores ATAS X) lorsqu'une
//         Heavy Absorption ou un Piégeage de Traders est confirmé par le Flux
//         d'Options ATAS X.
//    6. Panneau HUD temps réel + 8 Séries de données ATAS + Alertes sonores.
// =============================================================================

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
    [DisplayName("Footprint Quant OrderFlow, Options Flow & Auto-Bot (Max DD / Max PnL)")]
    [Category("Order Flow - Custom")]
    [Description("Indicateur & Bot Quant Senior Order Flow pour ATAS X (.NET 10) : HVN/LVN, Heavy Absorption, Traders Piégés, Flux d'Options ATAS X, Modèle Quant Z-Score/ATR/CVD et Bot de Trading Automatique avec Max Drawdown & Max PnL.")]
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
        private const string GrpTrappedVisual = "8. Acheteurs & Vendeurs Piégés (Visuel & Rayons)";
        private const string GrpOptionsAlerts = "9. Alerte Visuelle Achat/Vente & Flux d'Options ATAS X";
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
        // 5. HEAVY ABSORPTION (DÉTECTION SUR FOOTPRINT)
        // =====================================================================
        private bool _enableAbsorption = true;
        [Display(Name = "Activer Heavy Absorption", GroupName = GrpAbsorption, Order = 10)]
        public bool EnableAbsorption
        {
            get => _enableAbsorption;
            set { _enableAbsorption = value; RecalculateValues(); }
        }

        private int _absMinVolume = 0;
        [Display(Name = "Volume Minimum du Niveau Absorbé", GroupName = GrpAbsorption, Order = 20,
            Description = "Volume minimum (Bid+Ask) sur le prix pour qualifier une Heavy Absorption (0 = utilise uniquement le multiplicateur relatif).")]
        [Range(0, 1000000)]
        public int AbsMinVolume
        {
            get => _absMinVolume;
            set { _absMinVolume = value; RecalculateValues(); }
        }

        private decimal _absVolumeMultiplier = 1.5m;
        [Display(Name = "Multiplicateur de Volume vs Moyenne Bougie", GroupName = GrpAbsorption, Order = 30,
            Description = "Le volume du cluster doit être au moins X fois supérieur au volume moyen par niveau de la bougie (ex: 1.5x).")]
        [Range(1.0, 10.0)]
        public decimal AbsVolumeMultiplier
        {
            get => _absVolumeMultiplier;
            set { _absVolumeMultiplier = value; RecalculateValues(); }
        }

        private int _absMinDominantPct = 60;
        [Display(Name = "Dominance Minimale Bid ou Ask (%)", GroupName = GrpAbsorption, Order = 40,
            Description = "Pourcentage minimum du côté agressif absorbé : Bid/(Bid+Ask) en bas ou Ask/(Bid+Ask) en haut.")]
        [Range(50, 99)]
        public int AbsMinDominantPct
        {
            get => _absMinDominantPct;
            set { _absMinDominantPct = value; RecalculateValues(); }
        }

        private int _absMinAbsDelta = 0;
        [Display(Name = "|Delta| Minimum sur le Niveau", GroupName = GrpAbsorption, Order = 50)]
        [Range(0, 500000)]
        public int AbsMinAbsDelta
        {
            get => _absMinAbsDelta;
            set { _absMinAbsDelta = value; RecalculateValues(); }
        }

        private decimal _absDiagonalRatio = 1.6m;
        [Display(Name = "Ratio d'Imbalance Diagonale Min", GroupName = GrpAbsorption, Order = 60,
            Description = "Ratio diagonal Bid vs Ask supérieur (ou Ask vs Bid inférieur). Mettre 1.0 pour désactiver ce filtre.")]
        [Range(1.0, 20.0)]
        public decimal AbsDiagonalRatio
        {
            get => _absDiagonalRatio;
            set { _absDiagonalRatio = value; RecalculateValues(); }
        }

        private int _absZonePct = 35;
        [Display(Name = "Zone d'Extrême de la Bougie (% du Range)", GroupName = GrpAbsorption, Order = 70,
            Description = "Position du cluster absorbé : dans les X% inférieurs de la bougie (absorption acheteuse) ou X% supérieurs (absorption vendeuse).")]
        [Range(10, 100)]
        public int AbsZonePct
        {
            get => _absZonePct;
            set { _absZonePct = value; RecalculateValues(); }
        }

        private int _absLocationTicks = 3;
        [Display(Name = "OU Distance Max de l'Extrême (en niveaux)", GroupName = GrpAbsorption, Order = 80,
            Description = "Qualifie aussi le niveau s'il est à moins de N niveaux du High ou du Low de la bougie.")]
        [Range(0, 25)]
        public int AbsLocationTicks
        {
            get => _absLocationTicks;
            set { _absLocationTicks = value; RecalculateValues(); }
        }

        private bool _requirePriceRejection = true;
        [Display(Name = "Exiger Rejet du Prix (Clôture opposée)", GroupName = GrpAbsorption, Order = 90,
            Description = "Pour une absorption basse (Bullish), la clôture doit être au-dessus du niveau absorbé. Pour une absorption haute (Bearish), la clôture doit être en dessous.")]
        public bool RequirePriceRejection
        {
            get => _requirePriceRejection;
            set { _requirePriceRejection = value; RecalculateValues(); }
        }

        private bool _allowMidBarWallAbsorption = true;
        [Display(Name = "Détecter aussi les Murs d'Absorption Intra-Bar", GroupName = GrpAbsorption, Order = 100,
            Description = "Détecte une absorption massive même au cœur de la bougie si le volume dépasse 2.4x la moyenne avec fort rejet du prix.")]
        public bool AllowMidBarWallAbsorption
        {
            get => _allowMidBarWallAbsorption;
            set { _allowMidBarWallAbsorption = value; RecalculateValues(); }
        }

        // =====================================================================
        // 6. HEAVY ABSORPTION (VISUEL & RAYONS)
        // =====================================================================
        [Display(Name = "Couleur Heavy Absorption Bullish (Bid absorbé)", GroupName = GrpAbsVisual, Order = 10)]
        public Color BullishAbsorptionColor { get; set; } = Color.FromArgb(255, 0, 240, 255); // Cyan Néon

        [Display(Name = "Couleur Heavy Absorption Bearish (Ask absorbé)", GroupName = GrpAbsVisual, Order = 20)]
        public Color BearishAbsorptionColor { get; set; } = Color.FromArgb(255, 255, 45, 135); // Magenta / Rose Néon

        [Display(Name = "Opacité surbrillance Absorption (0-255)", GroupName = GrpAbsVisual, Order = 30)]
        [Range(30, 255)]
        public int AbsHighlightAlpha { get; set; } = 135;

        [Display(Name = "Épaisseur bordure cellule absorbée (px)", GroupName = GrpAbsVisual, Order = 40)]
        [Range(1, 6)]
        public int AbsBorderThickness { get; set; } = 2;

        [Display(Name = "Afficher marqueur au-dessus/sous la bougie", GroupName = GrpAbsVisual, Order = 50)]
        public bool ShowBarExtremeMarker { get; set; } = true;

        [Display(Name = "Style du marqueur de bougie", GroupName = GrpAbsVisual, Order = 60)]
        public AbsorptionMarkerType BarMarkerStyle { get; set; } = AbsorptionMarkerType.Diamond;

        [Display(Name = "Taille du marqueur (px)", GroupName = GrpAbsVisual, Order = 70)]
        [Range(4, 24)]
        public int BarMarkerSize { get; set; } = 9;

        [Display(Name = "Projeter Rayon S/R d'Absorption vers la droite", GroupName = GrpAbsVisual, Order = 80)]
        public bool ProjectAbsorptionRays { get; set; } = true;

        [Display(Name = "Longueur max du rayon d'absorption (bougies)", GroupName = GrpAbsVisual, Order = 90)]
        [Range(2, 200)]
        public int AbsorptionRayMaxBars { get; set; } = 25;

        [Display(Name = "Arrêter le rayon dès que le niveau est cassé", GroupName = GrpAbsVisual, Order = 100)]
        public bool StopRayWhenMitigated { get; set; } = true;

        // =====================================================================
        // 7. ACHETEURS & VENDEURS PIÉGÉS (DÉTECTION)
        // =====================================================================
        private bool _enableTrappedTraders = true;
        [Display(Name = "Activer Acheteurs & Vendeurs Piégés", GroupName = GrpTrappedDetect, Order = 10,
            Description = "Détecte les acheteurs agressifs piégés en haut de bougie (signal baissier) et les vendeurs agressifs piégés en bas de bougie (signal haussier).")]
        public bool EnableTrappedTraders
        {
            get => _enableTrappedTraders;
            set { _enableTrappedTraders = value; RecalculateValues(); }
        }

        private int _trappedMinVolume = 0;
        [Display(Name = "Volume Minimum du Niveau Piégé", GroupName = GrpTrappedDetect, Order = 20,
            Description = "Volume minimum (Bid+Ask) sur le niveau pour valider un cluster d'acheteurs ou vendeurs piégés (0 = utilise le multiplicateur relatif).")]
        [Range(0, 1000000)]
        public int TrappedMinVolume
        {
            get => _trappedMinVolume;
            set { _trappedMinVolume = value; RecalculateValues(); }
        }

        private decimal _trappedVolumeMultiplier = 1.15m;
        [Display(Name = "Multiplicateur de Volume Min vs Moyenne Bougie", GroupName = GrpTrappedDetect, Order = 30,
            Description = "Le volume du niveau piégé doit être au moins X fois supérieur au volume moyen par niveau de la bougie (ex: 1.15x).")]
        [Range(0.5, 10.0)]
        public decimal TrappedVolumeMultiplier
        {
            get => _trappedVolumeMultiplier;
            set { _trappedVolumeMultiplier = value; RecalculateValues(); }
        }

        private int _trappedMinDominantPct = 58;
        [Display(Name = "Dominance Agressive Min Bid ou Ask (%)", GroupName = GrpTrappedDetect, Order = 40,
            Description = "Pourcentage minimum d'acheteurs agressifs (Ask%) en haut ou de vendeurs agressifs (Bid%) en bas pour qualifier le piège.")]
        [Range(50, 99)]
        public int TrappedMinDominantPct
        {
            get => _trappedMinDominantPct;
            set { _trappedMinDominantPct = value; RecalculateValues(); }
        }

        private decimal _trappedImbalanceRatio = 1.75m;
        [Display(Name = "Ratio d'Imbalance Diagonale Min (Piégés)", GroupName = GrpTrappedDetect, Order = 50,
            Description = "Imbalance diagonale Ask[i]/Bid[i-1] pour acheteurs piégés ou Bid[i]/Ask[i+1] pour vendeurs piégés (1.0 = désactivé).")]
        [Range(1.0, 20.0)]
        public decimal TrappedImbalanceRatio
        {
            get => _trappedImbalanceRatio;
            set { _trappedImbalanceRatio = value; RecalculateValues(); }
        }

        private int _trappedWickZonePct = 40;
        [Display(Name = "Zone de Piégeage Extrême (% du Range)", GroupName = GrpTrappedDetect, Order = 60,
            Description = "Le cluster piégé doit se situer dans la mèche ou dans les X% supérieurs (acheteurs piégés) / inférieurs (vendeurs piégés) de la bougie.")]
        [Range(10, 100)]
        public int TrappedWickZonePct
        {
            get => _trappedWickZonePct;
            set { _trappedWickZonePct = value; RecalculateValues(); }
        }

        private bool _requireStrictWickOrOppositeClose = true;
        [Display(Name = "Exiger niveau dans la mèche (hors corps) ou clôture opposée", GroupName = GrpTrappedDetect, Order = 70,
            Description = "Garantit que les acheteurs piégés sont au-dessus de la clôture (en perte latente immédiate) et les vendeurs piégés en dessous de la clôture.")]
        public bool RequireStrictWickOrOppositeClose
        {
            get => _requireStrictWickOrOppositeClose;
            set { _requireStrictWickOrOppositeClose = value; RecalculateValues(); }
        }

        private int _trappedMinRejectionLevels = 1;
        [Display(Name = "Recul Minimum de Clôture (en niveaux)", GroupName = GrpTrappedDetect, Order = 80,
            Description = "Nombre minimum de niveaux d'écart entre le cluster piégé et la clôture de la bougie confirmant que ces traders sont sous l'eau.")]
        [Range(1, 20)]
        public int TrappedMinRejectionLevels
        {
            get => _trappedMinRejectionLevels;
            set { _trappedMinRejectionLevels = value; RecalculateValues(); }
        }

        // =====================================================================
        // 8. ACHETEURS & VENDEURS PIÉGÉS (VISUEL & RAYONS)
        // =====================================================================
        [Display(Name = "Couleur Vendeurs Piégés (Signal Bullish ▲)", GroupName = GrpTrappedVisual, Order = 10)]
        public Color TrappedSellersBullColor { get; set; } = Color.FromArgb(255, 16, 245, 150); // Vert Émeraude Néon

        [Display(Name = "Couleur Acheteurs Piégés (Signal Bearish ▼)", GroupName = GrpTrappedVisual, Order = 20)]
        public Color TrappedBuyersBearColor { get; set; } = Color.FromArgb(255, 255, 90, 54); // Rouge / Orange Corail Vif

        [Display(Name = "Opacité surbrillance Traders Piégés (0-255)", GroupName = GrpTrappedVisual, Order = 30)]
        [Range(30, 255)]
        public int TrappedHighlightAlpha { get; set; } = 120;

        [Display(Name = "Afficher marqueur bougie Traders Piégés", GroupName = GrpTrappedVisual, Order = 40)]
        public bool ShowTrappedBarMarker { get; set; } = true;

        [Display(Name = "Projeter Rayon de Liquidation (Trapped Zone)", GroupName = GrpTrappedVisual, Order = 50,
            Description = "Projette une ligne pointillée depuis le prix des acheteurs/vendeurs piégés (zone de stop-loss / rachat forcé).")]
        public bool ProjectTrappedRays { get; set; } = true;

        [Display(Name = "Longueur max du rayon Traders Piégés (bougies)", GroupName = GrpTrappedVisual, Order = 60)]
        [Range(2, 150)]
        public int TrappedRayMaxBars { get; set; } = 20;

        // =====================================================================
        // 9. ALERTE VISUELLE ACHAT / VENTE & FLUX D'OPTIONS ATAS X
        // =====================================================================
        private bool _enableVisualBuySellAlerts = true;
        [Display(Name = "Activer Alertes Visuelles ACHAT ▲ / VENTE ▼", GroupName = GrpOptionsAlerts, Order = 10,
            Description = "Affiche un signal visuel ACHAT (BUY ▲) ou VENTE (SELL ▼) haute visibilité lorsque le Footprint (Absorption / Traders Piégés) entre en confluence avec le Flux d'Options ATAS X.")]
        public bool EnableVisualBuySellAlerts
        {
            get => _enableVisualBuySellAlerts;
            set { _enableVisualBuySellAlerts = value; RecalculateValues(); }
        }

        private bool _enableAtasOptionsFlow = true;
        [Display(Name = "Prendre en compte le Flux d'Options ATAS X", GroupName = GrpOptionsAlerts, Order = 20,
            Description = "Analyse le flux d'options fourni par ATAS X (IOptionsDataFeed Call/Put, StrikePrice, OpenInterest ΔOI, Sweeps & Block Trades) pour filtrer et scorer les signaux Achat/Vente.")]
        public bool EnableAtasOptionsFlow
        {
            get => _enableAtasOptionsFlow;
            set { _enableAtasOptionsFlow = value; RecalculateValues(); }
        }

        private bool _requireOptionsFlowConfirmation = true;
        [Display(Name = "Exiger confirmation du Flux d'Options (Calls / Puts)", GroupName = GrpOptionsAlerts, Order = 30,
            Description = "Un signal ACHAT exige un biais Call Flow haussier (PCR < 1 / Dominance Calls), et un signal VENTE exige un biais Put Flow baissier (PCR > 1 / Dominance Puts).")]
        public bool RequireOptionsFlowConfirmation
        {
            get => _requireOptionsFlowConfirmation;
            set { _requireOptionsFlowConfirmation = value; RecalculateValues(); }
        }

        private int _optionsMinDominancePct = 53;
        [Display(Name = "Dominance Min du Flux Calls ou Puts (%)", GroupName = GrpOptionsAlerts, Order = 40,
            Description = "Pourcentage minimum de flux Call (pour Achat) ou de flux Put (pour Vente) requis pour valider l'alerte visuelle.")]
        [Range(50, 95)]
        public int OptionsMinDominancePct
        {
            get => _optionsMinDominancePct;
            set { _optionsMinDominancePct = value; RecalculateValues(); }
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

        [Display(Name = "Activer les alertes sonores (Achat/Vente, Absorption & Piégés)", GroupName = GrpHudAlerts, Order = 50)]
        public bool EnableAlerts { get; set; } = true;

        [Display(Name = "Fichier son d'alerte", GroupName = GrpHudAlerts, Order = 60)]
        public string AlertSoundFile { get; set; } = "alert1.wav";

        // =====================================================================
        // 11. MODÈLE QUANT SENIOR (Z-SCORE, ATR, CVD & VWAP REGIME)
        // =====================================================================
        private bool _enableQuantFilter = true;
        [Display(Name = "Activer le Raffinement Quant Senior (Z-Score + CVD + ATR)", GroupName = GrpQuantEngine, Order = 10,
            Description = "Affine les signaux avec un modèle statistique institutionnel : Z-Score de volume/delta sur les clusters, divergence CVD, régime VWAP ± σ et volatilité ATR(14).")]
        public bool EnableQuantFilter
        {
            get => _enableQuantFilter;
            set { _enableQuantFilter = value; RecalculateValues(); }
        }

        private decimal _quantMinClusterZScore = 0.85m;
        [Display(Name = "Z-Score Minimum du Cluster (σ Volume/Delta)", GroupName = GrpQuantEngine, Order = 20,
            Description = "Nombre d'écarts-types (σ) minimum au-dessus de la moyenne de la bougie pour valider statistiquement le cluster d'absorption/piégeage.")]
        [Range(0.0, 5.0)]
        public decimal QuantMinClusterZScore
        {
            get => _quantMinClusterZScore;
            set { _quantMinClusterZScore = value; RecalculateValues(); }
        }

        private int _quantMinConfluenceScore = 62;
        [Display(Name = "Score Quant Composite Minimum (0-100%)", GroupName = GrpQuantEngine, Order = 30,
            Description = "Seuil de probabilité multi-facteurs (HVN/LVN + Absorption + Traders Piégés + Flux d'Options ATAS X + Z-Score/CVD) requis pour valider un signal.")]
        [Range(50, 95)]
        public int QuantMinConfluenceScore
        {
            get => _quantMinConfluenceScore;
            set { _quantMinConfluenceScore = value; RecalculateValues(); }
        }

        private int _quantAtrPeriod = 14;
        [Display(Name = "Période ATR (Volatilité Dynamique SL/TP)", GroupName = GrpQuantEngine, Order = 40)]
        [Range(5, 100)]
        public int QuantAtrPeriod
        {
            get => _quantAtrPeriod;
            set { _quantAtrPeriod = value; RecalculateValues(); }
        }

        private decimal _quantAtrStopMultiplier = 0.85m;
        [Display(Name = "Multiplicateur ATR pour Stop-Loss (SL)", GroupName = GrpQuantEngine, Order = 50,
            Description = "Calcule le Stop-Loss structurel sous/sur le niveau d'absorption/piégeage ajusté par X * ATR(14).")]
        [Range(0.2, 5.0)]
        public decimal QuantAtrStopMultiplier
        {
            get => _quantAtrStopMultiplier;
            set { _quantAtrStopMultiplier = value; RecalculateValues(); }
        }

        private decimal _quantTp1RiskReward = 0.75m;
        [Display(Name = "Ratio Risk/Reward TP1 (Prise de Profit Rapide + Profit-Lock)", GroupName = GrpQuantEngine, Order = 60,
            Description = "Distance de prise de profit TP1 haute probabilité (0.75R) : encaisse 50% (ou 100% si 1 lot) immédiatement et verrouille le Stop en gain.")]
        [Range(0.3, 10.0)]
        public decimal QuantTp1RiskReward
        {
            get => _quantTp1RiskReward;
            set { _quantTp1RiskReward = value; RecalculateValues(); }
        }

        private decimal _quantTp2RiskReward = 1.50m;
        [Display(Name = "Ratio Risk/Reward TP2 (Objectif Final + Trailing Stop)", GroupName = GrpQuantEngine, Order = 70,
            Description = "Objectif de prise de profit final TP2 (1.50R) accompagné d'un Trailing Stop dynamique verrouillant les gains tick par tick.")]
        [Range(0.6, 15.0)]
        public decimal QuantTp2RiskReward
        {
            get => _quantTp2RiskReward;
            set { _quantTp2RiskReward = value; RecalculateValues(); }
        }

        // =====================================================================
        // 12. BOT DE TRADING QUANT & RISK GUARD (MAX DRAWDOWN & MAX PNL)
        // =====================================================================
        private bool _enableTradingBot = true;
        [Display(Name = "Activer le Bot de Trading Quant", GroupName = GrpQuantBot, Order = 10,
            Description = "Active le moteur d'exécution automatique et le simulateur temps réel basé sur les signaux Quant Order Flow + Flux d'Options.")]
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
            public int AbsorptionSide; // +1 = Bullish (Bid absorbé en bas), -1 = Bearish (Ask absorbé en haut), 0 = Aucun
            public int TrappedSide;    // +1 = Vendeurs Piégés en bas (Signal Bullish ▲), -1 = Acheteurs Piégés en haut (Signal Bearish ▼), 0 = Aucun
            public decimal DominantPct;
            public decimal TrappedDominantPct;
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
            public int TrendBias; // +1 = Haussier (EMA8 >= EMA21 ou Close >= VWAP), -1 = Baissier, 0 = Neutre
            public decimal BarPocPrice;
            public int BullStackedImbalances;
            public int BearStackedImbalances;
        }

        private struct VisualTradeSignal
        {
            public int Bar;
            public int Direction; // +1 = ACHAT (BUY ▲), -1 = VENTE (SELL ▼), 0 = Aucun
            public decimal TriggerPrice;
            public decimal EntryPrice;
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
            public string ReasonTag;
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
        private int _liveTrackedDir = 0;
        private int _liveTrackedSignalBar = -1;
        private decimal _liveTrackedEntryPrice = 0m;
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

            // 1. Crypto Perpétuels / Spot (Binance, Bybit, OKX, Bitget...)
            if (s.Contains("USDT") || s.Contains("USDC") || s.Contains("BUSD") || s.Contains("FDUSD") ||
                s.Contains("PERP") || s.Contains("BINANCE") || s.Contains("BYBIT") ||
                s.StartsWith("BTCUSD") || s.StartsWith("ETHUSD") || s.StartsWith("SOLUSD"))
            {
                family = "CRYPTO";
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

            var crossCtx = DataProvider?.CrossTradingContext;
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
                        "CRYPTO" => MarketProfilePreset.CryptoBinanceFutures,
                        "ES" => MarketProfilePreset.CmeSp500_ES_MES,
                        "NQ" => MarketProfilePreset.CmeNasdaq_NQ_MNQ,
                        _ => MarketProfilePreset.CmeKeyFutures_GC_CL_YM_RTY
                    };
                }
                else
                {
                    // Heuristique par TickSize : ES/NQ/YM/RTY ont TickSize = 0.25 ou 1.0 ou 0.10 sans suffixe USDT
                    effectiveProfile = (tickSize == 0.25m)
                        ? MarketProfilePreset.CmeSp500_ES_MES
                        : MarketProfilePreset.CryptoBinanceFutures;
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
                        CrossTradingExecutionMode.ForceMicroOnNormalChart => CrossTradingExecutionMode.StandardChartContractOnly,
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

                var crossCtx = DataProvider?.CrossTradingContext;
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
                decimal lastPx = CurrentBar > 0
                    ? GetCandle(CurrentBar - 1).Close
                    : (TradingManager?.Security?.LastTradePrice ?? 0m);
                if (lastPx > 0m)
                    CheckLivePositionTargetsOnTick(lastPx);
            }
            RedrawChart();
        }

        protected override void OnNewMyTrade(MyTrade myTrade)
        {
            base.OnNewMyTrade(myTrade);
            CheckRealtimeAccountKillSwitch();
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
        // Réception temps réel des prints & blocks ATAS X (OnNewTrade / OnCumulativeTrade)
        // =====================================================================
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

            if (!_enableAtasOptionsFlow)
                return;

            int curBar = CurrentBar - 1;
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
            if (!_enableAtasOptionsFlow || trade == null || CurrentBar <= 0)
                return;

            int curBar = CurrentBar - 1;
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

                List<BotTradeExecutionRecord> simTrades = new List<BotTradeExecutionRecord>();
                BotPerformanceState botState = _enableTradingBot
                    ? SimulateQuantTradingBot(bar, tickSize, out simTrades)
                    : default;

                if (_enableTradingBot && _onlyShowExecutedBotSignals)
                {
                    bool openedOnThisBar = false;
                    for (int i = 0; i < simTrades.Count; i++)
                    {
                        if (simTrades[i].EntryBar == bar)
                        {
                            openedOnThisBar = true;
                            signal = simTrades[i].EntrySignal;
                            break;
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
                    VisualTradeSignal liveSignal = signal;
                    if (liveSignal.Direction == 0 && simTrades.Count > 0)
                    {
                        var lastSim = simTrades[simTrades.Count - 1];
                        if ((lastSim.EntryBar == bar || lastSim.EntryBar == bar - 1) &&
                            lastSim.ExitBar >= bar &&
                            lastSim.EntrySignal.Direction != 0)
                        {
                            liveSignal = lastSim.EntrySignal;
                        }
                    }
                    ProcessLiveTradingManagerBot(bar, candle, tickSize, liveSignal, botState);
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

            for (int i = 0; i < rows.Length; i++)
            {
                ClusterRow r = rows[i];
                if (r.Volume <= 0m)
                    continue;

                decimal askAggr = r.Ask;
                decimal bidAggr = r.Bid;

                if (!isCme)
                {
                    // --- Comportement 100% original préservé pour BTC Binance Futures / Crypto ---
                    if (r.AbsorptionSide > 0 || r.TrappedSide > 0)
                    {
                        callFlow += r.Volume * 0.85m;
                    }
                    else if (r.AbsorptionSide < 0 || r.TrappedSide < 0)
                    {
                        putFlow += r.Volume * 0.85m;
                    }

                    if (r.Price >= vwap)
                    {
                        callFlow += askAggr * 1.15m;
                        putFlow += bidAggr * 0.85m;
                    }
                    else
                    {
                        callFlow += askAggr * 0.90m;
                        putFlow += bidAggr * 1.15m;
                    }
                }
                else
                {
                    // --- Calibration Institutionnelle CME (ES/MES, NQ/MNQ, GC, CL) ---
                    // Sur le CME, les gros ordres limites passifs (Put Wall en bas / Call Wall en haut) absorbent les market orders :
                    // Un Bid massif sous le prix de clôture qui est rejeté vers le haut est une absorption institutionnelle HAUSSIÈRE (Call/Put-Write Flow).
                    if (r.AbsorptionSide > 0 || r.TrappedSide > 0)
                    {
                        callFlow += r.Volume * 1.35m;
                    }
                    else if (r.AbsorptionSide < 0 || r.TrappedSide < 0)
                    {
                        putFlow += r.Volume * 1.35m;
                    }

                    if (r.Price < candle.Close - tickSize * 0.5m)
                    {
                        // Niveaux situés sous la clôture (mèche basse rejetée / défense acheteuse CME)
                        bool isBullLevel = r.AbsorptionSide > 0 || r.TrappedSide > 0 || candle.Close >= candle.Open;
                        callFlow += askAggr * 1.15m + bidAggr * (isBullLevel ? 1.05m : 0.72m);
                        putFlow += bidAggr * (isBullLevel ? 0.30m : 0.75m);
                    }
                    else if (r.Price > candle.Close + tickSize * 0.5m)
                    {
                        // Niveaux situés au-dessus de la clôture (mèche haute rejetée / défense vendeuse CME)
                        bool isBearLevel = r.AbsorptionSide < 0 || r.TrappedSide < 0 || candle.Close <= candle.Open;
                        putFlow += bidAggr * 1.15m + askAggr * (isBearLevel ? 1.05m : 0.72m);
                        callFlow += askAggr * (isBearLevel ? 0.30m : 0.75m);
                    }
                    else
                    {
                        callFlow += askAggr * 1.12m;
                        putFlow += bidAggr * 1.12m;
                    }
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

            // Calcul EMA rapide (8) et EMA lente (21) pour qualifier le régime directionnel
            ComputeQuantEmas(bar, out decimal emaFast, out decimal emaSlow);
            metrics.EmaFast = emaFast;
            metrics.EmaSlow = emaSlow;

            bool aboveVwap = candle.Close >= vwap;
            bool belowVwap = candle.Close <= vwap;
            if (emaFast >= emaSlow && aboveVwap)
                metrics.TrendBias = 1;
            else if (emaFast <= emaSlow && belowVwap)
                metrics.TrendBias = -1;
            else if (emaFast > emaSlow || aboveVwap)
                metrics.TrendBias = 1;
            else if (emaFast < emaSlow || belowVwap)
                metrics.TrendBias = -1;
            else
                metrics.TrendBias = 0;

            // Divergence CVD d'Absorption + Shift de Delta Multi-Bougies (+ Tail Delta Recovery CME)
            bool passiveBullAbsCvd = (candle.Close >= candle.Open && candle.Delta < 0m) ||
                                     (isCme && candle.Close >= midPrice && candle.MinDelta < 0m && (candle.Delta - candle.MinDelta) >= Math.Abs(candle.MinDelta) * 0.45m);
            bool passiveBearAbsCvd = (candle.Close <= candle.Open && candle.Delta > 0m) ||
                                     (isCme && candle.Close <= midPrice && candle.MaxDelta > 0m && (candle.MaxDelta - candle.Delta) >= candle.MaxDelta * 0.45m);
            bool multiBarBullCvd = false;
            bool multiBarBearCvd = false;

            if (bar > 0)
            {
                var prevC = GetCandle(bar - 1);
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
            }

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

            return metrics;
        }

        private void ComputeQuantEmas(int bar, out decimal emaFast, out decimal emaSlow)
        {
            int start = Math.Max(0, bar - 34);
            decimal firstClose = GetCandle(start).Close;
            emaFast = firstClose;
            emaSlow = firstClose;

            const decimal kFast = 2m / (8m + 1m);
            const decimal kSlow = 2m / (21m + 1m);

            for (int b = start + 1; b <= bar; b++)
            {
                decimal c = GetCandle(b).Close;
                emaFast = (c - emaFast) * kFast + emaFast;
                emaSlow = (c - emaSlow) * kSlow + emaSlow;
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

        // =====================================================================
        // Évaluation du Signal Visuel & Quant Achat / Vente
        // (Stratégie Complète : HVN/LVN + Heavy Absorption + Traders Piégés + Flux d'Options ATAS X + Calibration CME & Micro Croisé)
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
            if (!_enableVisualBuySellAlerts || rows.Length == 0)
                return sig;

            // Si un ordre Live a déjà été exécuté sur cette bougie, verrouiller son signal exact pour éviter tout repainting
            if (_lockedExecutedSignalsByBar.TryGetValue(bar, out var lockedSig) && lockedSig.Direction != 0)
                return lockedSig;

            var contractInfo = ResolveContractAndCrossTradingInfo(tickSize);
            bool isCme = contractInfo.IsCmeMarket;
            MarketProfilePreset prof = contractInfo.EffectiveProfile;

            decimal step = tickSize * Math.Max(1, _ticksPerRow);
            decimal atr = Math.Max(step * 2m, opt.AtrPoints);
            decimal barRange = candle.High - candle.Low;

            // Garde de maturité sur la bougie en cours de formation (CurrentBar - 1)
            if (bar == CurrentBar - 1 && bar > 2)
            {
                if (rows.Length < 3)
                    return sig;

                int lb = Math.Min(10, bar);
                decimal sumVol = 0m;
                for (int k = 1; k <= lb; k++)
                    sumVol += GetCandle(bar - k).Volume;
                decimal avgBarVol = lb > 0 ? sumVol / lb : candle.Volume;
                decimal minLiveVolFactor = isCme ? 0.20m : 0.32m;
                if (avgBarVol > 0m && candle.Volume < avgBarVol * minLiveVolFactor)
                    return sig;
            }

            bool hasBullAbs = _enableAbsorption && bestBullAbsIdx >= 0;
            bool hasBearAbs = _enableAbsorption && bestBearAbsIdx >= 0;
            bool hasTrapSellers = _enableTrappedTraders && bestTrappedSellersIdx >= 0;
            bool hasTrapBuyers = _enableTrappedTraders && bestTrappedBuyersIdx >= 0;

            decimal midPrice = (candle.High + candle.Low) * 0.5m;
            decimal pocPrice = opt.BarPocPrice > 0m ? opt.BarPocPrice : midPrice;

            // Détection de la structure HVN / LVN de la bougie (Piliers 1 & 2 de la stratégie)
            bool hasBarHvn = false;
            bool hasLowerLvnRejection = false;
            bool hasUpperLvnRejection = false;
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].IsHvn || rows[i].IsPoc)
                    hasBarHvn = true;
                if (rows[i].IsLvn)
                {
                    if (rows[i].Price <= midPrice && candle.Close > rows[i].Price)
                        hasLowerLvnRejection = true;
                    if (rows[i].Price >= midPrice && candle.Close < rows[i].Price)
                        hasUpperLvnRejection = true;
                }
            }

            // Résolution de conflit si la même bougie présente des signaux opposés aux deux extrêmes
            bool bullWinsBar = !hasBearAbs || (candle.Close > candle.Open) || (candle.Close >= midPrice);
            bool bearWinsBar = !hasBullAbs || (candle.Close < candle.Open) || (candle.Close <= midPrice);

            decimal effMinClusterZ = isCme
                ? Math.Min(_quantMinClusterZScore, prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 0.55m : 0.65m)
                : _quantMinClusterZScore;
            int effMinConfScore = isCme
                ? Math.Min(_quantMinConfluenceScore, 64)
                : _quantMinConfluenceScore;

            string crossSuffix = contractInfo.IsCrossTradingMicro
                ? $" [CROISÉ➔{contractInfo.TradedSymbol}]"
                : string.Empty;

            // --- 1. Vérification Signal Quant ACHAT (BUY ▲) :
            //        Heavy Absorption Bid (ABS▲) et/ou Vendeurs Piégés (V.PIÉGÉS▲) + Structure HVN/LVN + Flux Options Calls ---
            if ((hasBullAbs || hasTrapSellers) && bullWinsBar)
            {
                ClusterRow keyRow = hasBullAbs ? rows[bestBullAbsIdx] : rows[bestTrappedSellersIdx];
                decimal clusterZ = Math.Max(keyRow.VolumeZScore, keyRow.DeltaZScore);
                bool hvnAnchored = keyRow.IsHvn || keyRow.IsPoc || (Math.Abs(keyRow.Price - pocPrice) <= step * 2m) || hasBarHvn;

                // Rejet haussier au-dessus du cluster d'absorption / vendeurs piégés
                bool bullRejectionOk = (candle.Close >= keyRow.Price + step * 0.5m || (isCme && candle.Close > candle.Low && candle.Close >= keyRow.Price)) &&
                                       (candle.Close >= candle.Open ||
                                        candle.Close >= candle.Low + barRange * (isCme ? 0.30m : 0.35m) ||
                                        candle.Close >= pocPrice - step);

                bool quantZOk = !_enableQuantFilter
                                || clusterZ >= effMinClusterZ
                                || (hasBullAbs && hasTrapSellers)
                                || (hvnAnchored && hasLowerLvnRejection);

                bool optionsOk = !_enableAtasOptionsFlow
                                 || !_requireOptionsFlowConfirmation
                                 || opt.OptionsBias > 0
                                 || (hasBullAbs && hasTrapSellers && opt.CallFlowPct >= (isCme ? 45m : 48m))
                                 || (opt.BullishCvdDivergence && opt.CallFlowPct >= (isCme ? 45m : 48m))
                                 || (opt.OiDelta > 0m && opt.CallFlowPct >= (isCme ? 46m : 49m))
                                 || (opt.SweepCount > 0 && opt.CallFlowPct >= (isCme ? 47m : 50m))
                                 || (isCme && hasBullAbs && candle.Close >= pocPrice && opt.CallFlowPct >= 46m);

                if (bullRejectionOk && quantZOk && optionsOk)
                {
                    int score = isCme ? 60 : 58;
                    if (hasBullAbs && hasTrapSellers) score += 16;
                    else if (hasBullAbs) score += 11;
                    else score += 10;

                    if (hvnAnchored) score += 5;
                    if (hasLowerLvnRejection) score += 4;
                    if (candle.Close >= pocPrice) score += 4;
                    if (opt.TrendBias > 0) score += 4;
                    if (opt.BullStackedImbalances >= 1) score += 3;
                    if (opt.CallFlowPct >= (isCme ? 52m : _optionsMinDominancePct))
                        score += (int)Math.Min(12m, (opt.CallFlowPct - 50m) * 0.60m);
                    if (clusterZ >= (isCme ? 0.9m : 1.1m))
                        score += (int)Math.Min(7m, clusterZ * 2.5m);
                    if (opt.BullishCvdDivergence)
                        score += 5;
                    if (opt.SweepCount > 0)
                        score += Math.Min(4, opt.SweepCount * 2);

                    score = Math.Clamp(score, 50, 99);

                    if (!_enableQuantFilter || score >= effMinConfScore)
                    {
                        decimal entryPrice = RoundToTick(candle.Close, tickSize);
                        decimal triggerPrice = entryPrice;

                        decimal stopPrice;
                        decimal tp1Price;
                        decimal tp2Price;

                        if (!isCme)
                        {
                            // --- Calibration originale 100% préservée pour BTC Binance Futures ---
                            decimal minStopDist = Math.Max(step * 3m, RoundToTick(atr * 0.35m, tickSize));
                            decimal maxStopDist = Math.Max(minStopDist + step, RoundToTick(atr * _quantAtrStopMultiplier, tickSize));
                            decimal structuralLow = Math.Min(candle.Low - step, keyRow.Price - step * 2m);
                            decimal rawStopDist = Math.Max(step * 2m, entryPrice - structuralLow);
                            decimal clampedStopDist = Math.Clamp(rawStopDist, minStopDist, maxStopDist);

                            stopPrice = RoundToTick(entryPrice - clampedStopDist, tickSize);
                            decimal riskPts = Math.Max(step * 2m, entryPrice - stopPrice);

                            decimal maxTp1Pts = Math.Max(step * 3m, RoundToTick(atr * 0.55m, tickSize));
                            decimal tp1Dist = Math.Clamp(RoundToTick(riskPts * _quantTp1RiskReward, tickSize), step * 2m, maxTp1Pts);
                            decimal maxTp2Pts = Math.Max(tp1Dist + step * 2m, RoundToTick(atr * 1.15m, tickSize));
                            decimal tp2Dist = Math.Clamp(RoundToTick(riskPts * _quantTp2RiskReward, tickSize), tp1Dist + step * 2m, maxTp2Pts);

                            tp1Price = RoundToTick(entryPrice + tp1Dist, tickSize);
                            tp2Price = RoundToTick(entryPrice + tp2Dist, tickSize);
                        }
                        else
                        {
                            // --- Calibration Institutionnelle CME (ES/MES, NQ/MNQ, GC/MGC, CL/MCL) ---
                            // Respiration adaptée au bruit de carnet CME (évite de se faire sortir par 2 ticks de bruit sur ES ou 4 ticks sur NQ)
                            int minStopSteps = prof switch
                            {
                                MarketProfilePreset.CmeNasdaq_NQ_MNQ => 14, // min 3.50 pts (14 ticks) sur NQ/MNQ
                                MarketProfilePreset.CmeSp500_ES_MES => 6,   // min 1.50 pts (6 ticks) sur ES/MES
                                _ => 8                                      // min 8 ticks sur GC/CL/YM/RTY
                            };
                            decimal minStopDist = Math.Max(step * minStopSteps, RoundToTick(atr * 0.45m, tickSize));
                            decimal cmeAtrStopMult = Math.Max(_quantAtrStopMultiplier, prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 1.45m : 1.35m);
                            decimal maxStopDist = Math.Max(minStopDist + step * 2m, RoundToTick(atr * cmeAtrStopMult, tickSize));
                            decimal structuralPad = prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? step * 3m : step * 2m;
                            decimal structuralLow = Math.Min(candle.Low - structuralPad, keyRow.Price - structuralPad);
                            decimal rawStopDist = Math.Max(minStopDist, entryPrice - structuralLow);
                            decimal clampedStopDist = Math.Clamp(rawStopDist, minStopDist, maxStopDist);

                            stopPrice = RoundToTick(entryPrice - clampedStopDist, tickSize);
                            decimal riskPts = Math.Max(minStopDist, entryPrice - stopPrice);

                            decimal cmeTp1Rr = Math.Max(0.95m, _quantTp1RiskReward);
                            decimal cmeTp2Rr = Math.Max(1.65m, _quantTp2RiskReward);
                            decimal maxTp1Pts = Math.Max(riskPts * 1.15m, RoundToTick(atr * 1.45m, tickSize));
                            decimal tp1Dist = Math.Clamp(RoundToTick(riskPts * cmeTp1Rr, tickSize), step * 4m, maxTp1Pts);
                            decimal maxTp2Pts = Math.Max(tp1Dist + step * 4m, RoundToTick(atr * 2.65m, tickSize));
                            decimal tp2Dist = Math.Clamp(RoundToTick(riskPts * cmeTp2Rr, tickSize), tp1Dist + step * 4m, maxTp2Pts);

                            tp1Price = RoundToTick(entryPrice + tp1Dist, tickSize);
                            tp2Price = RoundToTick(entryPrice + tp2Dist, tickSize);
                        }

                        string structTag = hasLowerLvnRejection ? "HVN+LVN▲" : "HVN/POC▲";
                        string reason = ((hasBullAbs && hasTrapSellers)
                            ? $"ABS▲+V.PIÉGÉS▲ + {structTag} + Z:{clusterZ:0.0}σ"
                            : (hasBullAbs ? $"ABS BID▲ + {structTag} + Z:{clusterZ:0.0}σ" : $"V.PIÉGÉS▲ + {structTag} + Z:{clusterZ:0.0}σ")) + crossSuffix;

                        return new VisualTradeSignal
                        {
                            Bar = bar,
                            Direction = 1,
                            TriggerPrice = triggerPrice,
                            EntryPrice = entryPrice,
                            StopPrice = stopPrice,
                            Tp1Price = tp1Price,
                            Tp2Price = tp2Price,
                            RiskRewardRatio = _quantTp2RiskReward,
                            ClusterZScore = clusterZ,
                            AtrValue = atr,
                            ConfluenceScore = score,
                            CallFlowPct = opt.CallFlowPct,
                            PutFlowPct = opt.PutFlowPct,
                            PutCallRatio = opt.PutCallRatio,
                            OiDelta = opt.OiDelta,
                            SweepCount = opt.SweepCount,
                            ReasonTag = reason
                        };
                    }
                }
            }

            // --- 2. Vérification Signal Quant VENTE (SELL ▼) :
            //        Heavy Absorption Ask (ABS▼) et/ou Acheteurs Piégés (A.PIÉGÉS▼) + Structure HVN/LVN + Flux Options Puts ---
            if ((hasBearAbs || hasTrapBuyers) && bearWinsBar)
            {
                ClusterRow keyRow = hasBearAbs ? rows[bestBearAbsIdx] : rows[bestTrappedBuyersIdx];
                decimal clusterZ = Math.Max(keyRow.VolumeZScore, keyRow.DeltaZScore);
                bool hvnAnchored = keyRow.IsHvn || keyRow.IsPoc || (Math.Abs(keyRow.Price - pocPrice) <= step * 2m) || hasBarHvn;

                // Rejet baissier en dessous du cluster d'absorption / acheteurs piégés
                bool bearRejectionOk = (candle.Close <= keyRow.Price - step * 0.5m || (isCme && candle.Close < candle.High && candle.Close <= keyRow.Price)) &&
                                       (candle.Close <= candle.Open ||
                                        candle.Close <= candle.High - barRange * (isCme ? 0.30m : 0.35m) ||
                                        candle.Close <= pocPrice + step);

                bool quantZOk = !_enableQuantFilter
                                || clusterZ >= effMinClusterZ
                                || (hasBearAbs && hasTrapBuyers)
                                || (hvnAnchored && hasUpperLvnRejection);

                bool optionsOk = !_enableAtasOptionsFlow
                                 || !_requireOptionsFlowConfirmation
                                 || opt.OptionsBias < 0
                                 || (hasBearAbs && hasTrapBuyers && opt.PutFlowPct >= (isCme ? 45m : 48m))
                                 || (opt.BearishCvdDivergence && opt.PutFlowPct >= (isCme ? 45m : 48m))
                                 || (opt.OiDelta < 0m && opt.PutFlowPct >= (isCme ? 46m : 49m))
                                 || (opt.SweepCount > 0 && opt.PutFlowPct >= (isCme ? 47m : 50m))
                                 || (isCme && hasBearAbs && candle.Close <= pocPrice && opt.PutFlowPct >= 46m);

                if (bearRejectionOk && quantZOk && optionsOk)
                {
                    int score = isCme ? 60 : 58;
                    if (hasBearAbs && hasTrapBuyers) score += 16;
                    else if (hasBearAbs) score += 11;
                    else score += 10;

                    if (hvnAnchored) score += 5;
                    if (hasUpperLvnRejection) score += 4;
                    if (candle.Close <= pocPrice) score += 4;
                    if (opt.TrendBias < 0) score += 4;
                    if (opt.BearStackedImbalances >= 1) score += 3;
                    if (opt.PutFlowPct >= (isCme ? 52m : _optionsMinDominancePct))
                        score += (int)Math.Min(12m, (opt.PutFlowPct - 50m) * 0.60m);
                    if (clusterZ >= (isCme ? 0.9m : 1.1m))
                        score += (int)Math.Min(7m, clusterZ * 2.5m);
                    if (opt.BearishCvdDivergence)
                        score += 5;
                    if (opt.SweepCount > 0)
                        score += Math.Min(4, opt.SweepCount * 2);

                    score = Math.Clamp(score, 50, 99);

                    if (!_enableQuantFilter || score >= effMinConfScore)
                    {
                        decimal entryPrice = RoundToTick(candle.Close, tickSize);
                        decimal triggerPrice = entryPrice;

                        decimal stopPrice;
                        decimal tp1Price;
                        decimal tp2Price;

                        if (!isCme)
                        {
                            // --- Calibration originale 100% préservée pour BTC Binance Futures ---
                            decimal minStopDist = Math.Max(step * 3m, RoundToTick(atr * 0.35m, tickSize));
                            decimal maxStopDist = Math.Max(minStopDist + step, RoundToTick(atr * _quantAtrStopMultiplier, tickSize));
                            decimal structuralHigh = Math.Max(candle.High + step, keyRow.Price + step * 2m);
                            decimal rawStopDist = Math.Max(step * 2m, structuralHigh - entryPrice);
                            decimal clampedStopDist = Math.Clamp(rawStopDist, minStopDist, maxStopDist);

                            stopPrice = RoundToTick(entryPrice + clampedStopDist, tickSize);
                            decimal riskPts = Math.Max(step * 2m, stopPrice - entryPrice);

                            decimal maxTp1Pts = Math.Max(step * 3m, RoundToTick(atr * 0.55m, tickSize));
                            decimal tp1Dist = Math.Clamp(RoundToTick(riskPts * _quantTp1RiskReward, tickSize), step * 2m, maxTp1Pts);
                            decimal maxTp2Pts = Math.Max(tp1Dist + step * 2m, RoundToTick(atr * 1.15m, tickSize));
                            decimal tp2Dist = Math.Clamp(RoundToTick(riskPts * _quantTp2RiskReward, tickSize), tp1Dist + step * 2m, maxTp2Pts);

                            tp1Price = RoundToTick(entryPrice - tp1Dist, tickSize);
                            tp2Price = RoundToTick(entryPrice - tp2Dist, tickSize);
                        }
                        else
                        {
                            // --- Calibration Institutionnelle CME (ES/MES, NQ/MNQ, GC/MGC, CL/MCL) ---
                            int minStopSteps = prof switch
                            {
                                MarketProfilePreset.CmeNasdaq_NQ_MNQ => 14, // min 3.50 pts (14 ticks) sur NQ/MNQ
                                MarketProfilePreset.CmeSp500_ES_MES => 6,   // min 1.50 pts (6 ticks) sur ES/MES
                                _ => 8
                            };
                            decimal minStopDist = Math.Max(step * minStopSteps, RoundToTick(atr * 0.45m, tickSize));
                            decimal cmeAtrStopMult = Math.Max(_quantAtrStopMultiplier, prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 1.45m : 1.35m);
                            decimal maxStopDist = Math.Max(minStopDist + step * 2m, RoundToTick(atr * cmeAtrStopMult, tickSize));
                            decimal structuralPad = prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? step * 3m : step * 2m;
                            decimal structuralHigh = Math.Max(candle.High + structuralPad, keyRow.Price + structuralPad);
                            decimal rawStopDist = Math.Max(minStopDist, structuralHigh - entryPrice);
                            decimal clampedStopDist = Math.Clamp(rawStopDist, minStopDist, maxStopDist);

                            stopPrice = RoundToTick(entryPrice + clampedStopDist, tickSize);
                            decimal riskPts = Math.Max(minStopDist, stopPrice - entryPrice);

                            decimal cmeTp1Rr = Math.Max(0.95m, _quantTp1RiskReward);
                            decimal cmeTp2Rr = Math.Max(1.65m, _quantTp2RiskReward);
                            decimal maxTp1Pts = Math.Max(riskPts * 1.15m, RoundToTick(atr * 1.45m, tickSize));
                            decimal tp1Dist = Math.Clamp(RoundToTick(riskPts * cmeTp1Rr, tickSize), step * 4m, maxTp1Pts);
                            decimal maxTp2Pts = Math.Max(tp1Dist + step * 4m, RoundToTick(atr * 2.65m, tickSize));
                            decimal tp2Dist = Math.Clamp(RoundToTick(riskPts * cmeTp2Rr, tickSize), tp1Dist + step * 4m, maxTp2Pts);

                            tp1Price = RoundToTick(entryPrice - tp1Dist, tickSize);
                            tp2Price = RoundToTick(entryPrice - tp2Dist, tickSize);
                        }

                        string structTag = hasUpperLvnRejection ? "HVN+LVN▼" : "HVN/POC▼";
                        string reason = ((hasBearAbs && hasTrapBuyers)
                            ? $"ABS▼+A.PIÉGÉS▼ + {structTag} + Z:{clusterZ:0.0}σ"
                            : (hasBearAbs ? $"ABS ASK▼ + {structTag} + Z:{clusterZ:0.0}σ" : $"A.PIÉGÉS▼ + {structTag} + Z:{clusterZ:0.0}σ")) + crossSuffix;

                        return new VisualTradeSignal
                        {
                            Bar = bar,
                            Direction = -1,
                            TriggerPrice = triggerPrice,
                            EntryPrice = entryPrice,
                            StopPrice = stopPrice,
                            Tp1Price = tp1Price,
                            Tp2Price = tp2Price,
                            RiskRewardRatio = _quantTp2RiskReward,
                            ClusterZScore = clusterZ,
                            AtrValue = atr,
                            ConfluenceScore = score,
                            CallFlowPct = opt.CallFlowPct,
                            PutFlowPct = opt.PutFlowPct,
                            PutCallRatio = opt.PutCallRatio,
                            OiDelta = opt.OiDelta,
                            SweepCount = opt.SweepCount,
                            ReasonTag = reason
                        };
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

            // Agrégation Micro-Cluster CME (2-3 ticks contigus sur NQ/MNQ et ES/MES lorsque le flux répartit un mur d'absorption sur plusieurs ticks)
            bool useCmePooling = isCme && _enableCmeMicroClusterPooling && _ticksPerRow == 1 && count >= 5;
            int poolSpan = (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ && count >= 16) ? 2 : 1;

            decimal GetPooledBid(int idx)
            {
                if (!useCmePooling) return rows[idx].Bid;
                decimal s = rows[idx].Bid;
                if (idx + 1 < count) s += rows[idx + 1].Bid * 0.65m;
                if (poolSpan >= 2 && idx + 2 < count) s += rows[idx + 2].Bid * 0.35m;
                return s;
            }

            decimal GetPooledAsk(int idx)
            {
                if (!useCmePooling) return rows[idx].Ask;
                decimal s = rows[idx].Ask;
                if (idx - 1 >= 0) s += rows[idx - 1].Ask * 0.65m;
                if (poolSpan >= 2 && idx - 2 >= 0) s += rows[idx - 2].Ask * 0.35m;
                return s;
            }

            // 1. Détection HVN Intra-Bougie
            if (_enableBarHvn)
            {
                decimal effHvnMinVol = isCme ? Math.Min(_hvnMinBarVolume, 5m) : _hvnMinBarVolume;
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

            // 2. Détection LVN Intra-Bougie (poches de faible liquidité entre deux clusters plus denses)
            if (_enableBarLvn && count >= 3)
            {
                decimal lvnCutoff = maxVol * (_lvnBarMaxPctOfPoc / 100m);
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

                    if (v <= prevV && v <= nextV && (v < prevV || v < nextV))
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

            // Paramètres effectifs (Crypto 100% original vs Calibration CME ES / NQ / GC / CL)
            decimal effAbsZonePct = isCme ? Math.Max(_absZonePct, 40m) : _absZonePct;
            int effAbsLocTicks = isCme
                ? (prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? Math.Max(_absLocationTicks, 8) : Math.Max(_absLocationTicks, 5))
                : _absLocationTicks;
            decimal effAbsMinVol = isCme
                ? Math.Min(_absMinVolume, prof == MarketProfilePreset.CmeSp500_ES_MES ? 12m : 6m)
                : _absMinVolume;
            decimal effAbsVolMult = isCme ? Math.Min(_absVolumeMultiplier, 1.32m) : _absVolumeMultiplier;
            decimal effAbsMinDelta = isCme
                ? Math.Min(_absMinAbsDelta, prof == MarketProfilePreset.CmeSp500_ES_MES ? 5m : 3m)
                : _absMinAbsDelta;
            decimal effAbsDomPct = isCme
                ? Math.Min(_absMinDominantPct, prof == MarketProfilePreset.CmeSp500_ES_MES ? 54m : 55m)
                : _absMinDominantPct;
            decimal effAbsDiag = isCme
                ? Math.Min(_absDiagonalRatio, prof == MarketProfilePreset.CmeSp500_ES_MES ? 1.28m : 1.32m)
                : _absDiagonalRatio;

            // 3. Détection Heavy Absorption sur le Footprint
            if (_enableAbsorption)
            {
                decimal lowerZoneLimit = candle.Low + barRange * (effAbsZonePct / 100m);
                decimal upperZoneLimit = candle.High - barRange * (effAbsZonePct / 100m);
                decimal minVolRequired = Math.Max(effAbsMinVol, avgLevelVol * effAbsVolMult);
                decimal wallVolRequired = Math.Max(effAbsMinVol, avgLevelVol * Math.Max(isCme ? 1.85m : 2.2m, effAbsVolMult * 1.30m));

                decimal bestBullVol = 0m;
                decimal bestBearVol = 0m;

                for (int i = 0; i < count; i++)
                {
                    decimal rawVol = rows[i].Volume;
                    decimal evalBid = isCme ? Math.Max(rows[i].Bid, GetPooledBid(i)) : rows[i].Bid;
                    decimal evalAsk = isCme ? Math.Max(rows[i].Ask, GetPooledAsk(i)) : rows[i].Ask;
                    decimal evalVol = isCme ? Math.Max(rawVol, evalBid + rows[i].Ask) : rawVol;

                    if (rawVol <= 0m || evalVol < minVolRequired)
                        continue;

                    decimal absDelta = Math.Max(Math.Abs(rows[i].Delta), isCme ? Math.Abs(evalBid - rows[i].Ask) : 0m);
                    if (absDelta < effAbsMinDelta)
                        continue;

                    decimal price = rows[i].Price;
                    decimal bid = rows[i].Bid;
                    decimal ask = rows[i].Ask;

                    // --- Test Bullish Heavy Absorption (Vendeurs agressifs au Bid absorbés) ---
                    bool inLowerExtreme = price <= lowerZoneLimit || (i <= effAbsLocTicks);
                    bool isMidWallBull = _allowMidBarWallAbsorption && evalVol >= wallVolRequired && candle.Close > price + step;

                    if ((inLowerExtreme || isMidWallBull) && (bid > ask || (useCmePooling && evalBid > ask * 1.22m)))
                    {
                        decimal bidPct = rawVol > 0m ? (bid / rawVol) * 100m : 0m;
                        decimal pooledBidPct = (evalBid + ask) > 0m ? (evalBid / (evalBid + ask)) * 100m : bidPct;
                        decimal domPct = Math.Max(bidPct, pooledBidPct);
                        rows[i].DominantPct = domPct;

                        bool passesDom = domPct >= effAbsDomPct;
                        bool passesRejection = !_requirePriceRejection || (candle.Close >= price && candle.Close > candle.Low);
                        bool passesDiag = true;

                        if (effAbsDiag > 1.0m && i + 1 < count)
                        {
                            if (!isCme)
                            {
                                if (rows[i + 1].Ask > 0m)
                                    passesDiag = (bid / rows[i + 1].Ask) >= effAbsDiag;
                            }
                            else
                            {
                                // Sur le CME (notamment NQ), un niveau balayé à 0 sur l'Ask diagonal est un Zero-Print Sweep valide
                                decimal diagDenom = Math.Max(1m, rows[i + 1].Ask);
                                passesDiag = (Math.Max(bid, evalBid) / diagDenom) >= effAbsDiag;
                            }
                        }

                        if (passesDom && passesRejection && passesDiag)
                        {
                            rows[i].AbsorptionSide = 1;
                            rows[i].VolumeZScore = Math.Max(rows[i].VolumeZScore, Math.Round((evalVol - avgLevelVol) / stdVol, 2));
                            if (evalVol > bestBullVol)
                            {
                                bestBullVol = evalVol;
                                bestBullAbsIndex = i;
                            }
                            continue;
                        }
                    }

                    // --- Test Bearish Heavy Absorption (Acheteurs agressifs à l'Ask absorbés) ---
                    bool inUpperExtreme = price >= upperZoneLimit || ((count - 1 - i) <= effAbsLocTicks);
                    bool isMidWallBear = _allowMidBarWallAbsorption && evalVol >= wallVolRequired && candle.Close < price - step;

                    if ((inUpperExtreme || isMidWallBear) && (ask > bid || (useCmePooling && evalAsk > bid * 1.22m)))
                    {
                        decimal askPct = rawVol > 0m ? (ask / rawVol) * 100m : 0m;
                        decimal pooledAskPct = (evalAsk + bid) > 0m ? (evalAsk / (evalAsk + bid)) * 100m : askPct;
                        decimal domPct = Math.Max(askPct, pooledAskPct);
                        rows[i].DominantPct = domPct;

                        bool passesDom = domPct >= effAbsDomPct;
                        bool passesRejection = !_requirePriceRejection || (candle.Close <= price && candle.Close < candle.High);
                        bool passesDiag = true;

                        if (effAbsDiag > 1.0m && i - 1 >= 0)
                        {
                            if (!isCme)
                            {
                                if (rows[i - 1].Bid > 0m)
                                    passesDiag = (ask / rows[i - 1].Bid) >= effAbsDiag;
                            }
                            else
                            {
                                decimal diagDenom = Math.Max(1m, rows[i - 1].Bid);
                                passesDiag = (Math.Max(ask, evalAsk) / diagDenom) >= effAbsDiag;
                            }
                        }

                        if (passesDom && passesRejection && passesDiag)
                        {
                            rows[i].AbsorptionSide = -1;
                            rows[i].VolumeZScore = Math.Max(rows[i].VolumeZScore, Math.Round((evalVol - avgLevelVol) / stdVol, 2));
                            if (evalVol > bestBearVol)
                            {
                                bestBearVol = evalVol;
                                bestBearAbsIndex = i;
                            }
                        }
                    }
                }
            }

            // 4. Détection des Acheteurs Piégés (Trapped Buyers ▼) & Vendeurs Piégés (Trapped Sellers ▲)
            if (_enableTrappedTraders && count >= 2)
            {
                decimal effTrapZonePct = isCme ? Math.Max(_trappedWickZonePct, 42m) : _trappedWickZonePct;
                decimal effTrapMinVol = isCme
                    ? Math.Min(_trappedMinVolume, prof == MarketProfilePreset.CmeSp500_ES_MES ? 10m : 5m)
                    : _trappedMinVolume;
                decimal effTrapVolMult = isCme ? Math.Min(_trappedVolumeMultiplier, 1.18m) : _trappedVolumeMultiplier;
                decimal effTrapDomPct = isCme
                    ? Math.Min(_trappedMinDominantPct, prof == MarketProfilePreset.CmeSp500_ES_MES ? 55m : 56m)
                    : _trappedMinDominantPct;
                decimal effTrapDiag = isCme
                    ? Math.Min(_trappedImbalanceRatio, prof == MarketProfilePreset.CmeSp500_ES_MES ? 1.30m : 1.35m)
                    : _trappedImbalanceRatio;

                decimal trapLowerZone = candle.Low + barRange * (effTrapZonePct / 100m);
                decimal trapUpperZone = candle.High - barRange * (effTrapZonePct / 100m);
                decimal trapMinVol = Math.Max(effTrapMinVol, avgLevelVol * effTrapVolMult);
                decimal bodyHigh = Math.Max(candle.Open, candle.Close);
                decimal bodyLow = Math.Min(candle.Open, candle.Close);
                decimal minRejectDist = Math.Max(1, _trappedMinRejectionLevels) * step;

                decimal bestTrapSellersDelta = 0m;
                decimal bestTrapBuyersDelta = 0m;

                for (int i = 0; i < count; i++)
                {
                    decimal rawVol = rows[i].Volume;
                    decimal evalBid = isCme ? Math.Max(rows[i].Bid, GetPooledBid(i)) : rows[i].Bid;
                    decimal evalAsk = isCme ? Math.Max(rows[i].Ask, GetPooledAsk(i)) : rows[i].Ask;
                    decimal evalVol = isCme ? Math.Max(rawVol, Math.Max(evalBid + rows[i].Ask, evalAsk + rows[i].Bid)) : rawVol;

                    if (rawVol <= 0m || evalVol < trapMinVol)
                        continue;

                    decimal price = rows[i].Price;
                    decimal bid = rows[i].Bid;
                    decimal ask = rows[i].Ask;

                    // --- A. Vendeurs Piégés en bas de bougie (Trapped Sellers ▲ -> Signal Haussier) ---
                    bool inLowerTrapZone = price <= trapLowerZone || price <= bodyLow;
                    bool closedAboveSellers = candle.Close >= price + minRejectDist;
                    bool inLowerWickOrBullClose = !_requireStrictWickOrOppositeClose || (price <= bodyLow || candle.Close > candle.Open);

                    if (inLowerTrapZone && closedAboveSellers && inLowerWickOrBullClose && (bid > ask || (useCmePooling && evalBid > ask * 1.25m)))
                    {
                        decimal bidPct = rawVol > 0m ? (bid / rawVol) * 100m : 0m;
                        decimal pooledPct = (evalBid + ask) > 0m ? (evalBid / (evalBid + ask)) * 100m : bidPct;
                        decimal domPct = Math.Max(bidPct, pooledPct);
                        bool passesDom = domPct >= effTrapDomPct;
                        bool passesDiag = true;

                        if (effTrapDiag > 1.0m && i + 1 < count)
                        {
                            if (!isCme)
                            {
                                if (rows[i + 1].Ask > 0m)
                                    passesDiag = (bid / rows[i + 1].Ask) >= effTrapDiag;
                            }
                            else
                            {
                                decimal diagDenom = Math.Max(1m, rows[i + 1].Ask);
                                passesDiag = (Math.Max(bid, evalBid) / diagDenom) >= effTrapDiag;
                            }
                        }

                        if (passesDom && passesDiag)
                        {
                            rows[i].TrappedSide = 1;
                            rows[i].TrappedDominantPct = domPct;
                            decimal sellDeltaAbs = Math.Max(bid - ask, evalBid - ask);
                            if (sellDeltaAbs > bestTrapSellersDelta)
                            {
                                bestTrapSellersDelta = sellDeltaAbs;
                                bestTrappedSellersIndex = i;
                            }
                            continue;
                        }
                    }

                    // --- B. Acheteurs Piégés en haut de bougie (Trapped Buyers ▼ -> Signal Baissier) ---
                    bool inUpperTrapZone = price >= trapUpperZone || price >= bodyHigh;
                    bool closedBelowBuyers = candle.Close <= price - minRejectDist;
                    bool inUpperWickOrBearClose = !_requireStrictWickOrOppositeClose || (price >= bodyHigh || candle.Close < candle.Open);

                    if (inUpperTrapZone && closedBelowBuyers && inUpperWickOrBearClose && (ask > bid || (useCmePooling && evalAsk > bid * 1.25m)))
                    {
                        decimal askPct = rawVol > 0m ? (ask / rawVol) * 100m : 0m;
                        decimal pooledPct = (evalAsk + bid) > 0m ? (evalAsk / (evalAsk + bid)) * 100m : askPct;
                        decimal domPct = Math.Max(askPct, pooledPct);
                        bool passesDom = domPct >= effTrapDomPct;
                        bool passesDiag = true;

                        if (effTrapDiag > 1.0m && i - 1 >= 0)
                        {
                            if (!isCme)
                            {
                                if (rows[i - 1].Bid > 0m)
                                    passesDiag = (ask / rows[i - 1].Bid) >= effTrapDiag;
                            }
                            else
                            {
                                decimal diagDenom = Math.Max(1m, rows[i - 1].Bid);
                                passesDiag = (Math.Max(ask, evalAsk) / diagDenom) >= effTrapDiag;
                            }
                        }

                        if (passesDom && passesDiag)
                        {
                            rows[i].TrappedSide = -1;
                            rows[i].TrappedDominantPct = domPct;
                            decimal buyDeltaAbs = Math.Max(ask - bid, evalAsk - bid);
                            if (buyDeltaAbs > bestTrapBuyersDelta)
                            {
                                bestTrapBuyersDelta = buyDeltaAbs;
                                bestTrappedBuyersIndex = i;
                            }
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
            int visibleBullAbs = 0;
            int visibleBearAbs = 0;
            int visibleTrappedSellers = 0;
            int visibleTrappedBuyers = 0;
            int visibleBuySignals = 0;
            int visibleSellSignals = 0;

            RaySignalEvent? latestAbsorption = null;
            RaySignalEvent? latestTrapped = null;
            VisualTradeSignal? latestTradeSignal = null;
            BarOptionsFlowMetrics latestBarOptMetrics = default;

            // 2. Boucle sur les bougies visibles : Rendu Footprint HVN, LVN, Heavy Absorption, Trapped Traders & Options Flow
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

                int barBestAbsSide = 0;
                int barBestTrapSide = 0;

                for (int i = 0; i < rows.Length; i++)
                {
                    ClusterRow r = rows[i];
                    if (r.Volume <= 0m && !r.IsLvn)
                        continue;

                    int yCenter = ChartInfo.GetYByPrice(r.Price, false);
                    int yTop = yCenter - rowHeight / 2;
                    Rectangle cellRect = new Rectangle(cellX, yTop, cellW, rowHeight);

                    bool hasSpecialOrderFlow = r.AbsorptionSide != 0 || r.TrappedSide != 0;

                    // --- A. Rendu LVN Intra-Bougie ---
                    if (r.IsLvn && !hasSpecialOrderFlow)
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
                    if (r.IsHvn && !hasSpecialOrderFlow)
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

                    // --- C. Rendu ACHETEURS & VENDEURS PIÉGÉS (Trapped Buyers / Sellers) ---
                    if (r.TrappedSide != 0)
                    {
                        bool isTrappedSellersBull = r.TrappedSide > 0;
                        if (isTrappedSellersBull) visibleTrappedSellers++; else visibleTrappedBuyers++;
                        barBestTrapSide = r.TrappedSide;

                        Color trapBase = isTrappedSellersBull ? TrappedSellersBullColor : TrappedBuyersBearColor;
                        Color trapFill = Color.FromArgb(TrappedHighlightAlpha, trapBase.R, trapBase.G, trapBase.B);
                        Color trapBorder = Color.FromArgb(255, trapBase.R, trapBase.G, trapBase.B);

                        if (r.AbsorptionSide == 0)
                        {
                            context.FillRectangle(trapFill, cellRect);
                            var trapPen = new RenderPen(trapBorder, 2f, DashStyle.DashDot);
                            context.DrawRectangle(trapPen, cellRect);

                            Rectangle rightAccent = new Rectangle(cellRect.Right - 4, cellRect.Top, 4, cellRect.Height);
                            context.FillRectangle(trapBorder, rightAccent);

                            if (ShowCellBadges && rowHeight >= CellFontSize - 1)
                            {
                                string badge = isTrappedSellersBull
                                    ? $"V.PIÉGÉS▲ {r.Bid:0}x{r.Ask:0} ({r.TrappedDominantPct:0}%)"
                                    : $"A.PIÉGÉS▼ {r.Bid:0}x{r.Ask:0} ({r.TrappedDominantPct:0}%)";
                                Size bSz = context.MeasureString(badge, boldFont);
                                int bx = cellW >= bSz.Width + 8 ? cellRect.Left + 5 : cellRect.Right + 4;
                                int by = yCenter - bSz.Height / 2;
                                context.DrawString(badge, boldFont, Color.White, bx, by);
                            }
                        }

                        var trapEv = new RaySignalEvent
                        {
                            Bar = bar,
                            Price = r.Price,
                            Volume = r.Volume,
                            Delta = r.Delta,
                            Side = r.TrappedSide,
                            IsTrapped = true
                        };
                        activeTrappedRays.Add(trapEv);
                        latestTrapped = trapEv;
                    }

                    // --- D. Rendu HEAVY ABSORPTION (et Confluence ABS + PIÉGÉ) sur le Footprint ---
                    if (r.AbsorptionSide != 0)
                    {
                        bool isBull = r.AbsorptionSide > 0;
                        if (isBull) visibleBullAbs++; else visibleBearAbs++;
                        barBestAbsSide = r.AbsorptionSide;

                        Color absBase = isBull ? BullishAbsorptionColor : BearishAbsorptionColor;
                        Color absFill = Color.FromArgb(AbsHighlightAlpha, absBase.R, absBase.G, absBase.B);
                        Color absBorder = Color.FromArgb(255, absBase.R, absBase.G, absBase.B);

                        context.FillRectangle(absFill, cellRect);
                        var absPen = new RenderPen(absBorder, AbsBorderThickness);
                        context.DrawRectangle(absPen, cellRect);

                        Rectangle accentBar = new Rectangle(cellRect.Left, cellRect.Top, Math.Max(3, AbsBorderThickness + 1), cellRect.Height);
                        context.FillRectangle(absBorder, accentBar);

                        if (r.TrappedSide != 0)
                        {
                            Color trapCol = r.TrappedSide > 0 ? TrappedSellersBullColor : TrappedBuyersBearColor;
                            Rectangle rightTrapBar = new Rectangle(cellRect.Right - 4, cellRect.Top, 4, cellRect.Height);
                            context.FillRectangle(trapCol, rightTrapBar);
                        }

                        if (ShowCellBadges && rowHeight >= CellFontSize - 1)
                        {
                            string prefix = r.TrappedSide != 0
                                ? (isBull ? "ABS+PIÉGÉ▲" : "ABS+PIÉGÉ▼")
                                : (isBull ? "ABS▲" : "ABS▼");
                            string badge = $"{prefix} {r.Bid:0}x{r.Ask:0} ({r.DominantPct:0}%)";
                            Size bSz = context.MeasureString(badge, boldFont);
                            int bx = cellW >= bSz.Width + 8 ? cellRect.Left + 5 : cellRect.Right + 4;
                            int by = yCenter - bSz.Height / 2;
                            context.DrawString(badge, boldFont, Color.White, bx, by);
                        }

                        var ev = new RaySignalEvent
                        {
                            Bar = bar,
                            Price = r.Price,
                            Volume = r.Volume,
                            Delta = r.Delta,
                            Side = r.AbsorptionSide,
                            IsTrapped = false
                        };
                        activeAbsRays.Add(ev);
                        latestAbsorption = ev;
                    }

                    // --- E. Texte Bid x Ask intégré optionnel (si mode chandelier) ---
                    if (RenderBuiltInBidAskText && !hasSpecialOrderFlow && rowHeight >= CellFontSize && cellW >= 48)
                    {
                        string baText = $"{r.Bid:0} x {r.Ask:0}";
                        Size sz = context.MeasureString(baText, cellFont);
                        Color txtColor = r.Delta >= 0m
                            ? Color.FromArgb(220, 130, 240, 170)
                            : Color.FromArgb(220, 245, 140, 140);
                        context.DrawString(baText, cellFont, txtColor, xMid - sz.Width / 2, yCenter - sz.Height / 2);
                    }
                }

                // Marqueur d'absorption au sommet ou au creux de la bougie
                if (ShowBarExtremeMarker && barBestAbsSide != 0)
                {
                    Color markerCol = barBestAbsSide > 0 ? BullishAbsorptionColor : BearishAbsorptionColor;
                    DrawExtremeMarker(context, candle, barBestAbsSide, markerCol, xMid, rowHeight, BarMarkerStyle, 0);
                }

                // Marqueur des Acheteurs / Vendeurs Piégés au sommet ou au creux de la bougie
                if (ShowTrappedBarMarker && barBestTrapSide != 0)
                {
                    Color trapCol = barBestTrapSide > 0 ? TrappedSellersBullColor : TrappedBuyersBearColor;
                    int extraOffset = (ShowBarExtremeMarker && barBestAbsSide == barBestTrapSide) ? (BarMarkerSize * 2 + 4) : 0;
                    DrawExtremeMarker(context, candle, barBestTrapSide, trapCol, xMid, rowHeight, AbsorptionMarkerType.Arrow, extraOffset);
                }

                // Évaluation du signal visuel Achat / Vente prenant en compte le Flux d'Options ATAS X
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

            // 3. Projection des rayons Support / Résistance des Heavy Absorptions
            if (ProjectAbsorptionRays && activeAbsRays.Count > 0)
            {
                RenderSignalRays(context, activeAbsRays, lastBar, step, cellFont, AbsorptionRayMaxBars, false);
            }

            // 4. Projection des rayons de liquidation des Acheteurs / Vendeurs Piégés
            if (ProjectTrappedRays && activeTrappedRays.Count > 0)
            {
                RenderSignalRays(context, activeTrappedRays, lastBar, step, cellFont, TrappedRayMaxBars, true);
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
                botPerf.StatusLabel = "DÉSACTIVÉ (Cliquez sur [BOT QUANT: OFF] sur la charte)";
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
            int minVisible = FirstVisibleBarNumber >= 0 ? FirstVisibleBarNumber : upToBar;
            int lookbackStart = Math.Max(0, Math.Min(minVisible, upToBar - Math.Max(220, ProfileLookbackBars * 4)));
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
            decimal entryPrice = 0m;
            decimal stopPrice = 0m;
            decimal initialRiskPts = step * 2m;
            decimal tp1Price = 0m;
            decimal tp2Price = 0m;
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

                // 1. Gestion dynamique de la position ouverte du Bot sur la bougie b
                if (activeDir != 0 && b > entryBar)
                {
                    decimal tp1DistPts = Math.Max(step * 2m, Math.Abs(tp1Price - entryPrice));
                    bool stopTriggered = activeDir > 0 ? (c.Low <= stopPrice) : (c.High >= stopPrice);
                    bool tp2Triggered = activeDir > 0 ? (c.High >= tp2Price) : (c.Low <= tp2Price);
                    bool tp1Triggered = !tp1Hit && (activeDir > 0 ? (c.High >= tp1Price) : (c.Low <= tp1Price));
                    bool favorableClose = activeDir > 0 ? (c.Close >= c.Open) : (c.Close <= c.Open);
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
                            tp2Price, totalTradePnl, "TP2 TARGET", true, activeSignal);

                        activeDir = 0;
                        activeQty = 0;
                        bankedPartialUsd = 0m;
                    }
                    else if (tp1Triggered && !stopTriggered)
                    {
                        tp1Hit = true;

                        // Si 1 seul contrat (ou sortie complète sur TP1), encaisse 100% du profit immédiatement sur TP1
                        if (activeQty == 1 || !_botAutoBreakevenOnTp1)
                        {
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
                        else
                        {
                            // Multi-contrats : encaisse 50% immédiatement sur TP1 et verrouille le Stop du solde en gain
                            int half = activeQty / 2;
                            bankedPartialUsd += activeDir * (tp1Price - entryPrice) * pointVal * half
                                - (commPerSide * 2m * half);
                            activeQty -= half;

                            decimal lockOffset = isCme
                                ? Math.Max(step * 2m, RoundToTick(tp1DistPts * 0.35m, tickSize))
                                : Math.Max(step * 2m, RoundToTick(tp1DistPts * 0.45m, tickSize));
                            stopPrice = RoundToTick(entryPrice + activeDir * lockOffset, tickSize);

                            bool hitProfitLockAfterTp1 = activeDir > 0
                                ? (c.Close <= stopPrice && c.Low <= stopPrice)
                                : (c.Close >= stopPrice && c.High >= stopPrice);

                            if (hitProfitLockAfterTp1)
                            {
                                decimal remPnl = activeDir * (stopPrice - entryPrice) * pointVal * activeQty
                                    - (commPerSide * 2m * activeQty);
                                decimal totalTradePnl = Math.Round(bankedPartialUsd + remPnl, 2);

                                FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                                    entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                                    stopPrice, totalTradePnl, "TP1 + PROFIT LOCK", true, activeSignal);

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

                        FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                            entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                            stopPrice, totalTradePnl, tp1Hit ? "TP1 + TRAIL PROFIT" : "STOP LOSS", tp1Hit, activeSignal);

                        activeDir = 0;
                        activeQty = 0;
                        bankedPartialUsd = 0m;
                    }

                    // Protection dynamique : Early Profit-Lock & Trailing Stop après TP1
                    if (activeDir != 0)
                    {
                        decimal maxFavExcursion = activeDir > 0 ? (c.High - entryPrice) : (entryPrice - c.Low);
                        if (!isCme)
                        {
                            // --- Comportement 100% original pour BTC Binance Futures ---
                            if (!tp1Hit && maxFavExcursion >= Math.Max(step * 2m, initialRiskPts * 0.45m))
                            {
                                decimal softBe = RoundToTick(entryPrice + activeDir * step, tickSize);
                                if ((activeDir > 0 && softBe > stopPrice) || (activeDir < 0 && softBe < stopPrice))
                                    stopPrice = softBe;
                            }
                            else if (tp1Hit)
                            {
                                decimal trailGap = Math.Max(step * 2m, RoundToTick(tp1DistPts * 0.45m, tickSize));
                                decimal favPeak = activeDir > 0 ? c.High : c.Low;
                                decimal candidateTrail = RoundToTick(favPeak - activeDir * trailGap, tickSize);

                                if (activeDir > 0 && candidateTrail > stopPrice)
                                    stopPrice = candidateTrail;
                                else if (activeDir < 0 && candidateTrail < stopPrice)
                                    stopPrice = candidateTrail;
                            }
                        }
                        else
                        {
                            // --- Calibration CME (ES/MES, NQ/MNQ) : évite l'arrêt prématuré sur la respiration normale du carnet CME ---
                            int minBeTriggerSteps = prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 10 : 5;
                            int beLockSteps = prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 3 : 2;
                            if (!tp1Hit && maxFavExcursion >= Math.Max(step * minBeTriggerSteps, initialRiskPts * 0.68m))
                            {
                                decimal softBe = RoundToTick(entryPrice + activeDir * (step * beLockSteps), tickSize);
                                if ((activeDir > 0 && softBe > stopPrice) || (activeDir < 0 && softBe < stopPrice))
                                    stopPrice = softBe;
                            }
                            else if (tp1Hit)
                            {
                                int minTrailSteps = prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 8 : 4;
                                decimal trailGap = Math.Max(step * minTrailSteps, RoundToTick(tp1DistPts * 0.55m, tickSize));
                                decimal favPeak = activeDir > 0 ? c.High : c.Low;
                                decimal candidateTrail = RoundToTick(favPeak - activeDir * trailGap, tickSize);

                                if (activeDir > 0 && candidateTrail > stopPrice)
                                    stopPrice = candidateTrail;
                                else if (activeDir < 0 && candidateTrail < stopPrice)
                                    stopPrice = candidateTrail;
                            }
                        }
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

                var opt = AnalyzeBarOptionsFlow(b, c, rows, tickSize);
                var sig = EvaluateBuySellSignal(
                    b, c, rows, tickSize,
                    bullAbsIdx, bearAbsIdx,
                    trapSellIdx, trapBuyIdx,
                    opt);

                if (sig.Direction != 0)
                {
                    // Si une position opposée est ouverte et qu'un signal inverse validé par la stratégie apparaît, retournement immédiat
                    if (activeDir != 0 && activeDir != sig.Direction && b > entryBar)
                    {
                        decimal curOpenPnl = bankedPartialUsd + activeDir * (c.Close - entryPrice) * pointVal * activeQty;
                        decimal revPnl = curOpenPnl - (commPerSide * 2m * activeQty);

                        FinalizeBotTrade(ref state, tradeHistory, tradeReturns,
                            entryBar, b, activeDir, entryPrice, stopPrice, tp1Price, tp2Price,
                            c.Close, Math.Round(revPnl, 2), "REVERSAL SIGNAL", tp1Hit, activeSignal);

                        activeDir = 0;
                        activeQty = 0;
                        bankedPartialUsd = 0m;

                        if (state.IsHaltedByMaxDrawdown || state.IsHaltedByMaxProfit)
                            continue;
                    }

                    // Ouverture d'une nouvelle position Quant au prix exact du signal affiché
                    if (activeDir == 0)
                    {
                        activeDir = sig.Direction;
                        activeQty = initialQty;
                        entryBar = b;
                        entryPrice = sig.EntryPrice;
                        stopPrice = sig.StopPrice;
                        initialRiskPts = Math.Max(step * 2m, Math.Abs(sig.EntryPrice - sig.StopPrice));
                        tp1Price = sig.Tp1Price;
                        tp2Price = sig.Tp2Price;
                        tp1Hit = false;
                        bankedPartialUsd = 0m;
                        activeSignal = sig;
                    }
                }
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

            // En Trading Croisé ATAS X (ex: chart ES continu vs contrat Micro MES à échéance), s'il existe un écart de base (basis spread)
            // entre le prix du graphique et pos.AveragePrice, on conserve les niveaux SL/TP dans le référentiel de prix du graphique.
            bool hasCrossBasisOffset = contractInfo.IsCrossTradingMicro && curPrice > 0m && Math.Abs(rawFillPx - chartRefPx) > step * 12m;
            decimal fillPx = hasCrossBasisOffset ? chartRefPx : rawFillPx;

            if (_liveTrackedDir != realDir || _liveTrackedEntryPrice <= 0m || _liveTrackedStopPrice <= 0m || _liveTrackedTp1Price <= 0m || _liveTrackedTp2Price <= 0m)
            {
                int refBar = Math.Max(0, CurrentBar - 1);
                decimal atr = ComputeQuantAtr(refBar, tickSize);
                decimal riskPts;
                decimal tp1Dist;
                decimal tp2Dist;

                if (!isCme)
                {
                    riskPts = Math.Max(step * 3m, RoundToTick(atr * 0.55m, tickSize));
                    tp1Dist = Math.Clamp(RoundToTick(riskPts * _quantTp1RiskReward, tickSize), step * 2m, Math.Max(step * 3m, RoundToTick(atr * 0.55m, tickSize)));
                    tp2Dist = Math.Clamp(RoundToTick(riskPts * _quantTp2RiskReward, tickSize), tp1Dist + step * 2m, Math.Max(tp1Dist + step * 2m, RoundToTick(atr * 1.15m, tickSize)));
                }
                else
                {
                    int minStopSteps = prof switch
                    {
                        MarketProfilePreset.CmeNasdaq_NQ_MNQ => 14,
                        MarketProfilePreset.CmeSp500_ES_MES => 6,
                        _ => 8
                    };
                    riskPts = Math.Max(step * minStopSteps, RoundToTick(atr * 0.55m, tickSize));
                    tp1Dist = Math.Clamp(RoundToTick(riskPts * Math.Max(0.95m, _quantTp1RiskReward), tickSize), step * 4m, Math.Max(riskPts * 1.15m, RoundToTick(atr * 1.45m, tickSize)));
                    tp2Dist = Math.Clamp(RoundToTick(riskPts * Math.Max(1.65m, _quantTp2RiskReward), tickSize), tp1Dist + step * 4m, Math.Max(tp1Dist + step * 4m, RoundToTick(atr * 2.65m, tickSize)));
                }

                _liveTrackedDir = realDir;
                _liveTrackedEntryPrice = fillPx;
                _liveTrackedInitialRiskPts = riskPts;
                _liveTrackedStopPrice = RoundToTick(fillPx - realDir * riskPts, tickSize);
                _liveTrackedTp1Price = RoundToTick(fillPx + realDir * tp1Dist, tickSize);
                _liveTrackedTp2Price = RoundToTick(fillPx + realDir * tp2Dist, tickSize);
                _liveMaxFavorablePrice = curPrice > 0m ? curPrice : fillPx;
                _liveTrackedTp1Hit = false;
                _liveTp1PartialOrderSent = false;
                return;
            }

            // Recalage automatique des cibles TP1 / TP2 / SL sur le prix moyen d'exécution réel (si même référentiel de prix sans basis spread)
            if (pos.AveragePrice > 0m && !hasCrossBasisOffset && !_liveTrackedTp1Hit && Math.Abs(_liveTrackedEntryPrice - fillPx) >= tickSize)
            {
                decimal riskPts = Math.Max(step * 2m, Math.Abs(_liveTrackedEntryPrice - _liveTrackedStopPrice));
                decimal tp1Dist = Math.Max(step * 2m, Math.Abs(_liveTrackedTp1Price - _liveTrackedEntryPrice));
                decimal tp2Dist = Math.Max(tp1Dist + step * 2m, Math.Abs(_liveTrackedTp2Price - _liveTrackedEntryPrice));

                _liveTrackedEntryPrice = fillPx;
                _liveTrackedInitialRiskPts = riskPts;
                _liveTrackedStopPrice = RoundToTick(fillPx - realDir * riskPts, tickSize);
                _liveTrackedTp1Price = RoundToTick(fillPx + realDir * tp1Dist, tickSize);
                _liveTrackedTp2Price = RoundToTick(fillPx + realDir * tp2Dist, tickSize);
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
                bool isCme = contractInfo.IsCmeMarket;
                MarketProfilePreset prof = contractInfo.EffectiveProfile;
                decimal pointVal = contractInfo.TradedPointValueUsd;

                EnsureLivePositionTrackingInitialized(pos, livePrice, tickSize);
                if (_liveTrackedDir == 0)
                    return;

                int absQty = (int)Math.Round(Math.Abs(posVol));
                if (absQty <= 0)
                    return;

                int curBarIdx = Math.Max(0, CurrentBar - 1);
                decimal checkFavPrice = livePrice;
                if (CurrentBar > 0 && curBarIdx > _liveTrackedSignalBar)
                {
                    var curC = GetCandle(curBarIdx);
                    checkFavPrice = _liveTrackedDir > 0
                        ? Math.Max(livePrice, curC.High)
                        : Math.Min(livePrice, curC.Low);
                }

                if (_liveMaxFavorablePrice <= 0m)
                    _liveMaxFavorablePrice = checkFavPrice;
                else if (_liveTrackedDir > 0 && checkFavPrice > _liveMaxFavorablePrice)
                    _liveMaxFavorablePrice = checkFavPrice;
                else if (_liveTrackedDir < 0 && checkFavPrice < _liveMaxFavorablePrice)
                    _liveMaxFavorablePrice = checkFavPrice;

                decimal tp1DistPts = Math.Max(step * 2m, Math.Abs(_liveTrackedTp1Price - _liveTrackedEntryPrice));
                decimal tp2DistPts = Math.Max(tp1DistPts + step, Math.Abs(_liveTrackedTp2Price - _liveTrackedEntryPrice));
                decimal tp1TargetUsd = tp1DistPts * pointVal * absQty;
                decimal tp2TargetUsd = tp2DistPts * pointVal * absQty;
                decimal markToMarketUsd = _liveTrackedDir * (livePrice - _liveTrackedEntryPrice) * pointVal * absQty;
                decimal openUnrealizedUsd = (pos.UnrealizedPnL != 0m) ? pos.UnrealizedPnL : markToMarketUsd;

                bool tp2Hit = (_liveTrackedDir > 0 ? (checkFavPrice >= _liveTrackedTp2Price) : (checkFavPrice <= _liveTrackedTp2Price)) ||
                              (tp2TargetUsd > 0m && openUnrealizedUsd >= tp2TargetUsd);

                bool tp1Hit = !_liveTrackedTp1Hit &&
                              ((_liveTrackedDir > 0 ? (checkFavPrice >= _liveTrackedTp1Price) : (checkFavPrice <= _liveTrackedTp1Price)) ||
                               (tp1TargetUsd > 0m && openUnrealizedUsd >= tp1TargetUsd));

                // 1. Prise de profit finale TP2
                if (tp2Hit)
                {
                    _lastLiveExitSentUtc = DateTime.UtcNow;
                    _liveTrackedDir = 0;
                    _liveTrackedTp1Hit = false;
                    _liveTp1PartialOrderSent = false;
                    SendLiveMarketCloseOrReduce(tm, pos, absQty, $"QUANT_BOT_TP2_PROFIT_{contractInfo.TradedSymbol}");
                    return;
                }

                // 2. Prise de profit TP1 (Sortie 100% si 1 contrat, ou Sortie 50% + Profit-Lock Stop si >= 2 contrats)
                if (tp1Hit)
                {
                    _liveTrackedTp1Hit = true;
                    decimal lockOffset = isCme
                        ? Math.Max(step * 2m, RoundToTick(tp1DistPts * 0.35m, tickSize))
                        : Math.Max(step * 2m, RoundToTick(tp1DistPts * 0.45m, tickSize));
                    _liveTrackedStopPrice = RoundToTick(_liveTrackedEntryPrice + _liveTrackedDir * lockOffset, tickSize);

                    if (absQty == 1 || !_botAutoBreakevenOnTp1)
                    {
                        _lastLiveExitSentUtc = DateTime.UtcNow;
                        _liveTrackedDir = 0;
                        _liveTrackedTp1Hit = false;
                        _liveTp1PartialOrderSent = false;
                        SendLiveMarketCloseOrReduce(tm, pos, 1, $"QUANT_BOT_TP1_FULL_PROFIT_{contractInfo.TradedSymbol}");
                        return;
                    }
                    else if (!_liveTp1PartialOrderSent)
                    {
                        _liveTp1PartialOrderSent = true;
                        _lastLiveExitSentUtc = DateTime.UtcNow;
                        int halfQty = Math.Max(1, absQty / 2);
                        SendLiveMarketCloseOrReduce(tm, pos, halfQty, $"QUANT_BOT_TP1_50PCT_PROFIT_{contractInfo.TradedSymbol}");
                        return;
                    }
                }

                // 3. Mise à jour Early Profit-Lock et Trailing Stop dynamique après TP1
                decimal favExcursion = _liveTrackedDir > 0
                    ? (_liveMaxFavorablePrice - _liveTrackedEntryPrice)
                    : (_liveTrackedEntryPrice - _liveMaxFavorablePrice);

                if (!isCme)
                {
                    if (!_liveTrackedTp1Hit && favExcursion >= Math.Max(step * 2m, _liveTrackedInitialRiskPts * 0.45m))
                    {
                        decimal softBe = RoundToTick(_liveTrackedEntryPrice + _liveTrackedDir * step, tickSize);
                        if ((_liveTrackedDir > 0 && softBe > _liveTrackedStopPrice) ||
                            (_liveTrackedDir < 0 && softBe < _liveTrackedStopPrice))
                        {
                            _liveTrackedStopPrice = softBe;
                        }
                    }
                    else if (_liveTrackedTp1Hit)
                    {
                        decimal trailGap = Math.Max(step * 2m, RoundToTick(tp1DistPts * 0.45m, tickSize));
                        decimal candidateTrail = RoundToTick(_liveMaxFavorablePrice - _liveTrackedDir * trailGap, tickSize);
                        if ((_liveTrackedDir > 0 && candidateTrail > _liveTrackedStopPrice) ||
                            (_liveTrackedDir < 0 && candidateTrail < _liveTrackedStopPrice))
                        {
                            _liveTrackedStopPrice = candidateTrail;
                        }
                    }
                }
                else
                {
                    int minBeTriggerSteps = prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 10 : 5;
                    int beLockSteps = prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 3 : 2;
                    if (!_liveTrackedTp1Hit && favExcursion >= Math.Max(step * minBeTriggerSteps, _liveTrackedInitialRiskPts * 0.68m))
                    {
                        decimal softBe = RoundToTick(_liveTrackedEntryPrice + _liveTrackedDir * (step * beLockSteps), tickSize);
                        if ((_liveTrackedDir > 0 && softBe > _liveTrackedStopPrice) ||
                            (_liveTrackedDir < 0 && softBe < _liveTrackedStopPrice))
                        {
                            _liveTrackedStopPrice = softBe;
                        }
                    }
                    else if (_liveTrackedTp1Hit)
                    {
                        int minTrailSteps = prof == MarketProfilePreset.CmeNasdaq_NQ_MNQ ? 8 : 4;
                        decimal trailGap = Math.Max(step * minTrailSteps, RoundToTick(tp1DistPts * 0.55m, tickSize));
                        decimal candidateTrail = RoundToTick(_liveMaxFavorablePrice - _liveTrackedDir * trailGap, tickSize);
                        if ((_liveTrackedDir > 0 && candidateTrail > _liveTrackedStopPrice) ||
                            (_liveTrackedDir < 0 && candidateTrail < _liveTrackedStopPrice))
                        {
                            _liveTrackedStopPrice = candidateTrail;
                        }
                    }
                }

                // 4. Exécution du Stop-Loss ou du Trailing Stop en gain
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
                        ? $"QUANT_BOT_TRAIL_PROFIT_{contractInfo.TradedSymbol}"
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

                    _lastLiveBotOrderBar = sigBar;
                    _lastLiveBotOrderDir = signal.Direction;
                    _lockedExecutedSignalsByBar[sigBar] = signal;

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

                string entTxt = $"{(isBuy ? "ACHAT" : "VENTE")} @ {sig.EntryPrice}";
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
                ? $"★ QUANT ACHAT ▲ [{sig.ConfluenceScore}% | Z:{sig.ClusterZScore:0.0}σ]"
                : $"★ QUANT VENTE ▼ [{sig.ConfluenceScore}% | Z:{sig.ClusterZScore:0.0}σ]";
            string line2 = isBuy
                ? $"Calls {sig.CallFlowPct:0}% (PCR {sig.PutCallRatio:0.00}) | TP2 {sig.Tp2Price}"
                : $"Puts {sig.PutFlowPct:0}% (PCR {sig.PutCallRatio:0.00}) | TP2 {sig.Tp2Price}";

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
                ? $"▲ SIGNAL QUANT ACHAT @ {sig.EntryPrice} | SL: {sig.StopPrice} | TP1: {sig.Tp1Price} | TP2: {sig.Tp2Price} [{sig.ConfluenceScore}%]"
                : $"▼ SIGNAL QUANT VENTE @ {sig.EntryPrice} | SL: {sig.StopPrice} | TP1: {sig.Tp1Price} | TP2: {sig.Tp2Price} [{sig.ConfluenceScore}%]";

            string acctTag = !string.IsNullOrWhiteSpace(botPerf.ActiveAccountName) ? $" [{botPerf.ActiveAccountName}]" : string.Empty;
            string botTag = (_enableTradingBot || botPerf.IsSyncedWithActiveAccount)
                ? $" | PnL Réel{acctTag}: {botPerf.TotalPnlUsd:+0.00;-0.00;0.00}$ (Max {BotMaxProfitTargetUsd:0}$) • DD: -{botPerf.MaxObservedDrawdownUsd:0.00}$ (Max -{BotMaxDrawdownUsd:0}$)"
                : string.Empty;
            string detail = $"Options Flow : Calls {sig.CallFlowPct:0}% / Puts {sig.PutFlowPct:0}% (PCR {sig.PutCallRatio:0.00}, ΔOI {sig.OiDelta:+0;-0;0}) | {sig.ReasonTag}{botTag}";

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
            string optBiasTxt = latestOpt.OptionsBias > 0
                ? "CALLS HAUSSIER ▲"
                : (latestOpt.OptionsBias < 0 ? "PUTS BAISSIER ▼" : "NEUTRE");
            Color optBiasCol = latestOpt.OptionsBias > 0
                ? BuySignalColor
                : (latestOpt.OptionsBias < 0 ? SellSignalColor : Color.FromArgb(255, 200, 210, 225));

            decimal hudTickSize = InstrumentInfo != null && InstrumentInfo.TickSize > 0m ? InstrumentInfo.TickSize : 0.25m;
            var contractInfo = ResolveContractAndCrossTradingInfo(hudTickSize);

            var lines = new List<(string text, Color color, bool bold)>
            {
                ($"QUANT ORDERFLOW • OPTIONS • AUTO-BOT [{sym}]", Color.White, true),
                ($"MARCHÉ & CROISÉ   : {contractInfo.ProfileBadge}  |  {contractInfo.CrossTradingBadge} ({contractInfo.TradedPointValueUsd:0.##}$/pt)", Color.FromArgb(255, 250, 204, 21), true),
                ($"HVN Intra-Bar : {barHvnCount}  |  Zones HVN Profil : {compHvnCount}  |  VPOC : {(compPocPrice > 0m ? $"{compPocPrice}" : "N/A")}", BarHvnColor, false),
                ($"LVN Intra-Bar : {barLvnCount}  |  Zones LVN Profil : {compLvnCount}  |  ATR({_quantAtrPeriod}) : {latestOpt.AtrPoints:0.00}", BarLvnColor, false),
                ($"Heavy Abs Bull: {bullAbsCount} (Bid)  |  Bear: {bearAbsCount} (Ask)  |  Max Z-Score : {latestOpt.MaxVolumeZScore:0.0}σ", Color.FromArgb(255, 220, 230, 240), false),
                ($"Vendeurs Piégés ▲: {trappedSellersCount}  |  Acheteurs Piégés ▼: {trappedBuyersCount}  |  VWAP Z : {latestOpt.VwapZScore:+0.0;-0.0;0.0}σ", Color.FromArgb(255, 16, 245, 150), false),
                ($"Flux Options ATAS X : Calls {latestOpt.CallFlowPct:0}% / Puts {latestOpt.PutFlowPct:0}% (PCR {latestOpt.PutCallRatio:0.00}) [{optBiasTxt}]", optBiasCol, true),
                ($"Signaux Quant       : ACHAT ▲ {buySignalsCount}  |  VENTE ▼ {sellSignalsCount}  (ΔOI {latestOpt.OiDelta:+0;-0;0}, Sweeps {latestOpt.SweepCount})", Color.FromArgb(255, 125, 211, 252), false)
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
                lines.Add(($"STATS QUANT BOT     : Trades {botPerf.TotalTrades} ({botPerf.WinRatePct:0}% Win) | PF {botPerf.ProfitFactor:0.00} | Sharpe {botPerf.SharpeRatio:0.00} | Exp {botPerf.ExpectancyUsd:+0;-0;0}$", Color.FromArgb(255, 203, 213, 225), false));
            }
            else
            {
                lines.Add(("BOT STATUS          : DÉSACTIVÉ (Cliquez sur [BOT QUANT: OFF] sur la charte pour activer)", Color.FromArgb(255, 148, 163, 184), true));
                lines.Add(($"HORAIRES CONFIGURÉS : {BotStartTimeText} -> {BotEndTimeText} ({(_enableBotSchedule ? "ON" : "OFF")})", Color.FromArgb(255, 148, 163, 184), false));
            }

            if (latestSignal.HasValue)
            {
                var s = latestSignal.Value;
                bool isBuy = s.Direction > 0;
                string tag = isBuy ? "★ DERNIER SIGNAL ACHAT ▲" : "★ DERNIER SIGNAL VENTE ▼";
                Color c = isBuy ? BuySignalColor : SellSignalColor;
                lines.Add(($"{tag}: Entrée {s.EntryPrice} | SL {s.StopPrice} | TP1 {s.Tp1Price} | TP2 {s.Tp2Price} [{s.ConfluenceScore}%]", c, true));
            }
            else if (latestAbs.HasValue)
            {
                var a = latestAbs.Value;
                bool bull = a.Side > 0;
                string tag = bull ? "▲ DERNIÈRE ABS BULL" : "▼ DERNIÈRE ABS BEAR";
                Color c = bull ? BullishAbsorptionColor : BearishAbsorptionColor;
                lines.Add(($"{tag}: {a.Price} | Vol {a.Volume:0} | Δ {a.Delta:+0;-0;0}", c, true));
            }

            if (latestTrapped.HasValue)
            {
                var t = latestTrapped.Value;
                bool bull = t.Side > 0;
                string tag = bull ? "▲ VENDEURS PIÉGÉS" : "▼ ACHETEURS PIÉGÉS";
                Color c = bull ? TrappedSellersBullColor : TrappedBuyersBearColor;
                lines.Add(($"{tag}: {t.Price} | Vol {t.Volume:0} | Δ {t.Delta:+0;-0;0}", c, true));
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
                    ? "BOT QUANT : STOP DD MAX"
                    : (botPerf.IsHaltedByMaxProfit ? "BOT QUANT : LOCK PNL MAX" : "BOT QUANT : ON (ACTIF)"))
                : "BOT QUANT : OFF (CLIQUEZ)";

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
                CrossTradingExecutionMode.AutoAtasCrossOrMicro => ctrlContractInfo.IsCrossTradingActive
                    ? $"CROISÉ: {ctrlContractInfo.ChartSymbol}➔{ctrlContractInfo.TradedSymbol}"
                    : "CROISÉ: AUTO ATAS X",
                CrossTradingExecutionMode.ForceMicroOnStandardChart => $"CROISÉ: MICRO ({ctrlContractInfo.TradedSymbol})",
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
            bool isCrossOn = ctrlContractInfo.IsCrossTradingActive || _crossTradingMode == CrossTradingExecutionMode.ForceMicroOnStandardChart;
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
    }
}
