// =============================================================================
//  Footprint HVN / LVN & Heavy Absorption — Indicateur Order Flow pour ATAS X
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
//         (Flèche, Point, Losange, Crochet) et rayon S/R projeté jusqu'à
//         mitigation (test du niveau) ou sur N bougies.
//    4. Panneau HUD temps réel + Séries de données ATAS + Alertes sonores.
// =============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.Drawing.Drawing2D;
using ATAS.Indicators;
using OFT.Attributes;
using OFT.Rendering.Context;
using OFT.Rendering.Settings;
using OFT.Rendering.Tools;

namespace ATAS.Indicators.Custom
{
    [DisplayName("Footprint HVN / LVN & Heavy Absorption")]
    [Category("Order Flow - Custom")]
    [Description("Détecte et affiche les HVN (High Volume Nodes), LVN (Low Volume Nodes) et les Heavy Absorptions directement sur le Footprint ATAS X.")]
    public class FootprintHvnLvnAbsorption : Indicator
    {
        // =====================================================================
        // Groupes de paramètres dans l'interface ATAS X
        // =====================================================================
        private const string GrpGeneral    = "1. Général & Footprint";
        private const string GrpBarHvn     = "2. HVN sur Footprint (Intra-Bougie)";
        private const string GrpBarLvn     = "3. LVN sur Footprint (Intra-Bougie)";
        private const string GrpProfile    = "4. HVN / LVN Profil Composite (Multi-Bougies)";
        private const string GrpAbsorption = "5. Heavy Absorption (Détection)";
        private const string GrpAbsVisual  = "6. Heavy Absorption (Visuel & Rayons)";
        private const string GrpHudAlerts  = "7. Panneau HUD & Alertes";

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
        private int _ticksPerRow = 1;
        [Display(Name = "Regroupement (Ticks par niveau)", GroupName = GrpGeneral, Order = 10,
            Description = "Nombre de ticks agrégés par ligne de prix sur le footprint (1 = tick par tick).")]
        [Range(1, 50)]
        public int TicksPerRow
        {
            get => _ticksPerRow;
            set { _ticksPerRow = Math.Max(1, value); RecalculateValues(); }
        }

        [Display(Name = "Afficher texte Bid x Ask intégré", GroupName = GrpGeneral, Order = 20,
            Description = "Activez si votre graphique est en chandeliers classiques et que vous souhaitez afficher les volumes Bid x Ask dans les cellules.")]
        public bool RenderBuiltInBidAskText { get; set; } = false;

        [Display(Name = "Afficher étiquettes sur cellules (HVN/LVN/ABS)", GroupName = GrpGeneral, Order = 30)]
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

        private decimal _absVolumeMultiplier = 1.6m;
        [Display(Name = "Multiplicateur de Volume vs Moyenne Bougie", GroupName = GrpAbsorption, Order = 30,
            Description = "Le volume du cluster doit être au moins X fois supérieur au volume moyen par niveau de la bougie (ex: 1.6x).")]
        [Range(1.0, 10.0)]
        public decimal AbsVolumeMultiplier
        {
            get => _absVolumeMultiplier;
            set { _absVolumeMultiplier = value; RecalculateValues(); }
        }

        private int _absMinDominantPct = 62;
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

        private decimal _absDiagonalRatio = 1.8m;
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
        // 7. PANNEAU HUD & ALERTES
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

        [Display(Name = "Activer les alertes sonores ATAS (Heavy Absorption)", GroupName = GrpHudAlerts, Order = 50)]
        public bool EnableAlerts { get; set; } = true;

        [Display(Name = "Fichier son d'alerte", GroupName = GrpHudAlerts, Order = 60)]
        public string AlertSoundFile { get; set; } = "alert1.wav";

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

        // =====================================================================
        // Structures internes d'analyse par niveau de prix
        // =====================================================================
        private struct ClusterRow
        {
            public decimal Price;
            public decimal Bid;
            public decimal Ask;
            public decimal Volume;
            public decimal Delta;
            public bool IsPoc;
            public bool IsHvn;
            public bool IsLvn;
            public int AbsorptionSide; // +1 = Bullish (Bid absorbé en bas), -1 = Bearish (Ask absorbé en haut), 0 = Aucun
            public decimal DominantPct;
        }

        private struct AbsorptionRayEvent
        {
            public int Bar;
            public decimal Price;
            public decimal Volume;
            public decimal Delta;
            public int Side; // +1 Bullish, -1 Bearish
        }

        private int _lastAlertBar = -1;
        private int _lastAlertSide = 0;

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

            var candle = GetCandle(bar);
            var rows = AnalyzeBarClusters(candle, tickSize, out int pocIdx, out int primaryLvnIdx, out int bestBullAbsIdx, out int bestBearAbsIdx);

            if (rows.Length == 0)
                return;

            if (_enableBarHvn && pocIdx >= 0)
                _pocHvnSeries[bar] = rows[pocIdx].Price;
            else
                _pocHvnSeries[bar] = 0m;

            if (_enableBarLvn && primaryLvnIdx >= 0)
                _barLvnSeries[bar] = rows[primaryLvnIdx].Price;
            else
                _barLvnSeries[bar] = 0m;

            if (_enableAbsorption && bestBullAbsIdx >= 0)
                _bullAbsSeries[bar] = rows[bestBullAbsIdx].Price;
            else
                _bullAbsSeries[bar] = 0m;

            if (_enableAbsorption && bestBearAbsIdx >= 0)
                _bearAbsSeries[bar] = rows[bestBearAbsIdx].Price;
            else
                _bearAbsSeries[bar] = 0m;

            // Alerte temps réel sur la dernière bougie
            if (EnableAlerts && bar == CurrentBar - 1)
            {
                int side = bestBullAbsIdx >= 0 ? 1 : (bestBearAbsIdx >= 0 ? -1 : 0);
                if (side != 0 && (_lastAlertBar != bar || _lastAlertSide != side))
                {
                    _lastAlertBar = bar;
                    _lastAlertSide = side;

                    ClusterRow absRow = side > 0 ? rows[bestBullAbsIdx] : rows[bestBearAbsIdx];
                    string dirText = side > 0 ? "BULLISH HEAVY ABSORPTION (Bid Absorbé)" : "BEARISH HEAVY ABSORPTION (Ask Absorbé)";
                    string instrument = InstrumentInfo?.Instrument ?? "ATAS X";
                    string msg = $"{dirText} @ {absRow.Price} | Vol: {absRow.Volume:0} (Bid {absRow.Bid:0} x Ask {absRow.Ask:0}, Δ {absRow.Delta:+0;-0;0})";
                    Color bg = side > 0 ? Color.FromArgb(255, 0, 95, 115) : Color.FromArgb(255, 120, 20, 65);
                    AddAlert(AlertSoundFile, instrument, msg, bg, Color.White);
                }
            }
        }

        // =====================================================================
        // Analyse complète des niveaux Footprint d'une bougie (HVN, LVN, Absorption)
        // =====================================================================
        private ClusterRow[] AnalyzeBarClusters(
            IndicatorCandle candle,
            decimal tickSize,
            out int pocIndex,
            out int primaryLvnIndex,
            out int bestBullAbsIndex,
            out int bestBearAbsIndex)
        {
            pocIndex = -1;
            primaryLvnIndex = -1;
            bestBullAbsIndex = -1;
            bestBearAbsIndex = -1;

            int ticksPerLevel = Math.Max(1, _ticksPerRow);
            decimal step = tickSize * ticksPerLevel;
            if (step <= 0m || candle.High < candle.Low)
                return Array.Empty<ClusterRow>();

            int count = (int)Math.Floor((candle.High - candle.Low) / step) + 1;
            if (count <= 0 || count > 2000)
                return Array.Empty<ClusterRow>();

            var rows = new ClusterRow[count];
            decimal maxVol = 0m;
            decimal totalBarVol = 0m;
            int activeLevels = 0;

            for (int i = 0; i < count; i++)
            {
                decimal basePrice = candle.Low + i * step;
                decimal bidSum = 0m;
                decimal askSum = 0m;

                for (int t = 0; t < ticksPerLevel; t++)
                {
                    decimal p = basePrice + t * tickSize;
                    if (p > candle.High + tickSize * 0.1m)
                        break;

                    var info = candle.GetPriceVolumeInfo(p);
                    if (info != null)
                    {
                        bidSum += info.Bid;
                        askSum += info.Ask;
                    }
                }

                decimal vol = bidSum + askSum;
                decimal delta = askSum - bidSum;

                rows[i] = new ClusterRow
                {
                    Price = basePrice,
                    Bid = bidSum,
                    Ask = askSum,
                    Volume = vol,
                    Delta = delta
                };

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

            // 1. Détection HVN Intra-Bougie
            if (_enableBarHvn)
            {
                decimal hvnCutoff = maxVol * (_hvnBarPctOfPoc / 100m);
                for (int i = 0; i < count; i++)
                {
                    decimal v = rows[i].Volume;
                    if (v <= 0m || v < _hvnMinBarVolume || v < hvnCutoff)
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

                    // Creux local de volume entre deux voisins plus fournis
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

            // 3. Détection Heavy Absorption sur le Footprint
            if (_enableAbsorption)
            {
                decimal barRange = candle.High - candle.Low;
                decimal lowerZoneLimit = candle.Low + barRange * (_absZonePct / 100m);
                decimal upperZoneLimit = candle.High - barRange * (_absZonePct / 100m);
                decimal minVolRequired = Math.Max(_absMinVolume, avgLevelVol * _absVolumeMultiplier);
                decimal wallVolRequired = Math.Max(_absMinVolume, avgLevelVol * Math.Max(2.2m, _absVolumeMultiplier * 1.35m));

                decimal bestBullVol = 0m;
                decimal bestBearVol = 0m;

                for (int i = 0; i < count; i++)
                {
                    decimal vol = rows[i].Volume;
                    if (vol <= 0m || vol < minVolRequired)
                        continue;

                    decimal absDelta = Math.Abs(rows[i].Delta);
                    if (absDelta < _absMinAbsDelta)
                        continue;

                    decimal price = rows[i].Price;
                    decimal bid = rows[i].Bid;
                    decimal ask = rows[i].Ask;

                    // --- Test Bullish Heavy Absorption (Vendeurs agressifs au Bid absorbés) ---
                    bool inLowerExtreme = price <= lowerZoneLimit || (i <= _absLocationTicks);
                    bool isMidWallBull = _allowMidBarWallAbsorption && vol >= wallVolRequired && candle.Close > price + step;

                    if ((inLowerExtreme || isMidWallBull) && bid > ask)
                    {
                        decimal bidPct = (bid / vol) * 100m;
                        rows[i].DominantPct = bidPct;

                        bool passesDom = bidPct >= _absMinDominantPct;
                        bool passesRejection = !_requirePriceRejection || (candle.Close >= price && candle.Close > candle.Low);
                        bool passesDiag = true;

                        if (_absDiagonalRatio > 1.0m && i + 1 < count && rows[i + 1].Ask > 0m)
                        {
                            passesDiag = (bid / rows[i + 1].Ask) >= _absDiagonalRatio;
                        }

                        if (passesDom && passesRejection && passesDiag)
                        {
                            rows[i].AbsorptionSide = 1;
                            if (vol > bestBullVol)
                            {
                                bestBullVol = vol;
                                bestBullAbsIndex = i;
                            }
                            continue;
                        }
                    }

                    // --- Test Bearish Heavy Absorption (Acheteurs agressifs à l'Ask absorbés) ---
                    bool inUpperExtreme = price >= upperZoneLimit || ((count - 1 - i) <= _absLocationTicks);
                    bool isMidWallBear = _allowMidBarWallAbsorption && vol >= wallVolRequired && candle.Close < price - step;

                    if ((inUpperExtreme || isMidWallBear) && ask > bid)
                    {
                        decimal askPct = (ask / vol) * 100m;
                        rows[i].DominantPct = askPct;

                        bool passesDom = askPct >= _absMinDominantPct;
                        bool passesRejection = !_requirePriceRejection || (candle.Close <= price && candle.Close < candle.High);
                        bool passesDiag = true;

                        if (_absDiagonalRatio > 1.0m && i - 1 >= 0 && rows[i - 1].Bid > 0m)
                        {
                            passesDiag = (ask / rows[i - 1].Bid) >= _absDiagonalRatio;
                        }

                        if (passesDom && passesRejection && passesDiag)
                        {
                            rows[i].AbsorptionSide = -1;
                            if (vol > bestBearVol)
                            {
                                bestBearVol = vol;
                                bestBearAbsIndex = i;
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

            // Liste des absorptions détectées pour projeter les rayons S/R et alimenter le HUD
            var activeRays = new List<AbsorptionRayEvent>();
            int visibleHvnCells = 0;
            int visibleLvnCells = 0;
            int visibleBullAbs = 0;
            int visibleBearAbs = 0;
            AbsorptionRayEvent? latestAbsorption = null;

            // 2. Boucle sur les bougies visibles : Rendu Footprint HVN, LVN & Heavy Absorption
            for (int bar = firstBar; bar <= lastBar; bar++)
            {
                var candle = GetCandle(bar);
                var rows = AnalyzeBarClusters(candle, tickSize, out int pocIdx, out int lvnIdx, out int bullAbsIdx, out int bearAbsIdx);
                if (rows.Length == 0)
                    continue;

                int xStart = ChartInfo.GetXByBar(bar, true);
                int xEnd = ChartInfo.GetXByBar(bar + 1, true);
                int barSpan = xEnd - xStart;
                if (barSpan <= 4)
                    barSpan = Math.Max(8, (int)ChartInfo.PriceChartContainer.BarsWidth);

                int xMid = xStart + barSpan / 2;
                int cellW = CustomCellWidthPx > 0 ? CustomCellWidthPx : Math.Max(8, barSpan - 4);
                int cellX = xMid - cellW / 2;

                int barBestSide = 0;

                for (int i = 0; i < rows.Length; i++)
                {
                    ClusterRow r = rows[i];
                    if (r.Volume <= 0m && !r.IsLvn)
                        continue;

                    int yCenter = ChartInfo.GetYByPrice(r.Price, false);
                    int yTop = yCenter - rowHeight / 2;
                    Rectangle cellRect = new Rectangle(cellX, yTop, cellW, rowHeight);

                    // --- A. Rendu LVN Intra-Bougie ---
                    if (r.IsLvn && r.AbsorptionSide == 0)
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
                    if (r.IsHvn && r.AbsorptionSide == 0)
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

                    // --- C. Rendu HEAVY ABSORPTION sur le Footprint ---
                    if (r.AbsorptionSide != 0)
                    {
                        bool isBull = r.AbsorptionSide > 0;
                        if (isBull) visibleBullAbs++; else visibleBearAbs++;
                        barBestSide = r.AbsorptionSide;

                        Color absBase = isBull ? BullishAbsorptionColor : BearishAbsorptionColor;
                        Color absFill = Color.FromArgb(AbsHighlightAlpha, absBase.R, absBase.G, absBase.B);
                        Color absBorder = Color.FromArgb(255, absBase.R, absBase.G, absBase.B);

                        context.FillRectangle(absFill, cellRect);
                        var absPen = new RenderPen(absBorder, AbsBorderThickness);
                        context.DrawRectangle(absPen, cellRect);

                        // Barre latérale lumineuse d'intensité
                        Rectangle accentBar = new Rectangle(cellRect.Left, cellRect.Top, Math.Max(3, AbsBorderThickness + 1), cellRect.Height);
                        context.FillRectangle(absBorder, accentBar);

                        if (ShowCellBadges && rowHeight >= CellFontSize - 1)
                        {
                            string badge = isBull
                                ? $"ABS▲ {r.Bid:0}x{r.Ask:0} ({r.DominantPct:0}%)"
                                : $"ABS▼ {r.Bid:0}x{r.Ask:0} ({r.DominantPct:0}%)";
                            Size bSz = context.MeasureString(badge, boldFont);
                            int bx = cellW >= bSz.Width + 8 ? cellRect.Left + 5 : cellRect.Right + 4;
                            int by = yCenter - bSz.Height / 2;
                            context.DrawString(badge, boldFont, Color.White, bx, by);
                        }

                        var ev = new AbsorptionRayEvent
                        {
                            Bar = bar,
                            Price = r.Price,
                            Volume = r.Volume,
                            Delta = r.Delta,
                            Side = r.AbsorptionSide
                        };
                        activeRays.Add(ev);
                        latestAbsorption = ev;
                    }

                    // --- D. Texte Bid x Ask intégré optionnel (si mode chandelier) ---
                    if (RenderBuiltInBidAskText && r.AbsorptionSide == 0 && rowHeight >= CellFontSize && cellW >= 48)
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
                if (ShowBarExtremeMarker && barBestSide != 0)
                {
                    Color markerCol = barBestSide > 0 ? BullishAbsorptionColor : BearishAbsorptionColor;
                    DrawExtremeMarker(context, candle, barBestSide, markerCol, xMid, rowHeight);
                }
            }

            // 3. Projection des rayons Support / Résistance des Heavy Absorptions
            if (ProjectAbsorptionRays && activeRays.Count > 0)
            {
                RenderAbsorptionRays(context, activeRays, lastBar, step, cellFont);
            }

            // 4. Rendu du panneau HUD récapitulatif en temps réel
            if (ShowHudPanel)
            {
                RenderHud(
                    context, chartRegion,
                    visibleHvnCells, visibleLvnCells,
                    compositeHvnCount, compositeLvnCount,
                    compositePocPrice, compositePocVol,
                    visibleBullAbs, visibleBearAbs,
                    latestAbsorption);
            }
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
                for (decimal p = c.Low; p <= c.High; p += tickSize)
                {
                    var pv = c.GetPriceVolumeInfo(p);
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
        // Rayons Support/Résistance issus des Heavy Absorptions
        // =====================================================================
        private void RenderAbsorptionRays(
            RenderContext context,
            List<AbsorptionRayEvent> events,
            int lastVisibleBar,
            decimal step,
            RenderFont font)
        {
            for (int i = 0; i < events.Count; i++)
            {
                AbsorptionRayEvent ev = events[i];
                int endBar = Math.Min(CurrentBar - 1, ev.Bar + AbsorptionRayMaxBars);

                if (StopRayWhenMitigated)
                {
                    for (int b = ev.Bar + 1; b <= endBar; b++)
                    {
                        var c = GetCandle(b);
                        // Si une bougie suivante clôture franchement au-delà du niveau absorbé, le niveau est cassé/mitigé
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
                Color baseCol = ev.Side > 0 ? BullishAbsorptionColor : BearishAbsorptionColor;
                Color rayCol = Color.FromArgb(200, baseCol.R, baseCol.G, baseCol.B);

                var pen = new RenderPen(rayCol, 2f, DashStyle.Solid);
                context.DrawLine(pen, x1, y, x2, y);

                string rayLabel = ev.Side > 0
                    ? $"Bull ABS Support @ {ev.Price} (Vol {ev.Volume:0})"
                    : $"Bear ABS Resist @ {ev.Price} (Vol {ev.Volume:0})";
                Size sz = context.MeasureString(rayLabel, font);
                if (x2 - x1 >= sz.Width + 10)
                {
                    context.DrawString(rayLabel, font, baseCol, x2 - sz.Width - 4, y - sz.Height - 2);
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
            int rowHeight)
        {
            int size = BarMarkerSize;
            int gap = Math.Max(6, rowHeight / 2 + 4);
            int y = side > 0
                ? ChartInfo!.GetYByPrice(candle.Low, false) + gap + size
                : ChartInfo!.GetYByPrice(candle.High, false) - gap - size;

            switch (BarMarkerStyle)
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
            AbsorptionRayEvent? latestAbs)
        {
            var titleFont = new RenderFont("Consolas", 11, FontStyle.Bold);
            var bodyFont = new RenderFont("Consolas", 10);

            string sym = InstrumentInfo?.Instrument ?? "Instrument";
            var lines = new List<(string text, Color color, bool bold)>
            {
                ($"FOOTPRINT HVN / LVN / ABSORPTION [{sym}]", Color.White, true),
                ($"HVN Intra-Bar : {barHvnCount}  |  Zones HVN Profil : {compHvnCount}", BarHvnColor, false),
                ($"LVN Intra-Bar : {barLvnCount}  |  Zones LVN Profil : {compLvnCount}", BarLvnColor, false),
                ($"VPOC Composite: {(compPocPrice > 0m ? $"{compPocPrice} (Vol {compPocVol:0})" : "N/A")}", CompositeHvnColor, false),
                ($"Heavy Abs Bull: {bullAbsCount} (Bid)  |  Bear: {bearAbsCount} (Ask)", Color.FromArgb(255, 220, 230, 240), false)
            };

            if (latestAbs.HasValue)
            {
                var a = latestAbs.Value;
                bool bull = a.Side > 0;
                string tag = bull ? "▲ DERNIÈRE ABS BULL" : "▼ DERNIÈRE ABS BEAR";
                Color c = bull ? BullishAbsorptionColor : BearishAbsorptionColor;
                lines.Add(($"{tag}: {a.Price} | Vol {a.Volume:0} | Δ {a.Delta:+0;-0;0}", c, true));
            }
            else
            {
                lines.Add(("Aucune Heavy Absorption sur la plage visible", Color.FromArgb(255, 150, 160, 175), false));
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

            int boxW = maxW + pad * 2;
            int boxH = lines.Count * lineH + pad * 2;

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
            context.FillRectangle(Color.FromArgb(215, 13, 18, 30), boxRect);
            context.DrawRectangle(new RenderPen(Color.FromArgb(220, 56, 189, 248), 1.5f), boxRect);

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
        }
    }
}
