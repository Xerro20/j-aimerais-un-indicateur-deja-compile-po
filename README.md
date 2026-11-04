# Footprint HVN / LVN & Heavy Absorption — Indicateur pour ATAS X (.NET 10)

Indicateur Order Flow **déjà compilé** (`FootprintHvnLvnAbsorption.dll`) pour **ATAS X** (Windows & macOS, `.NET 10.0`) permettant d'identifier et de visualiser directement sur le **Footprint** :
1. **HVN (*High Volume Nodes*) & POC** — Nœuds de fort volume intra-bougie et zones HVN du profil de volume composite (session ou glissant).
2. **LVN (*Low Volume Nodes*)** — Creux de liquidité / *single prints* intra-bougie et vallées LVN du profil composite.
3. **Heavy Absorption (*Absorption Massive Bid / Ask*)** — Détection multi-critères des absorptions passives sur les extrêmes de bougie ou murs intra-bar, avec surbrillance néon du cluster, étiquette `ABS ▲ / ▼`, marqueur de bougie et rayon Support/Résistance projeté jusqu'à mitigation.

---

## 📥 Fichier `.dll` déjà compilé

Le binaire compilé prêt à l'emploi pour ATAS X est disponible à trois emplacements dans ce dépôt :
- [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll) *(racine du projet)*
- [`dist/FootprintHvnLvnAbsorption.dll`](./dist/FootprintHvnLvnAbsorption.dll)
- [`bin/Release/net10.0/FootprintHvnLvnAbsorption.dll`](./bin/Release/net10.0/FootprintHvnLvnAbsorption.dll)

| Propriété | Valeur |
| :--- | :--- |
| **Nom dans ATAS X** | `Footprint HVN / LVN & Heavy Absorption` |
| **Catégorie ATAS X** | `Order Flow - Custom` |
| **Framework cible** | `.NET 10.0` (`ATAS X` Windows & macOS) |
| **Taille du binaire** | `40 448 octets` (~39,5 Ko) |
| **SHA-256** | `c744068c521de62b4ab6433b0944139a5e0f4f1d4feb5eb527f87c003adb39fa` |

---

## 🚀 Installation dans ATAS X

### Sur Windows
1. Téléchargez le fichier [`FootprintHvnLvnAbsorption.dll`](./FootprintHvnLvnAbsorption.dll).
2. *(Optionnel)* Si Windows bloque le fichier téléchargé depuis Internet : Clic droit sur `FootprintHvnLvnAbsorption.dll` → **Propriétés** → cochez **Débloquer** (*Unblock*) en bas → **Appliquer**.
3. Copiez `FootprintHvnLvnAbsorption.dll` dans le dossier des indicateurs d'ATAS :
   ```text
   %USERPROFILE%\Documents\ATAS\Indicators
   ```
   *(Exemple : `C:\Users\VotreNom\Documents\ATAS\Indicators\`)*
4. Sur votre graphique Footprint dans **ATAS X**, ouvrez le gestionnaire d'indicateurs (**Ctrl+I** ou icône Indicateurs), cherchez **`Footprint HVN / LVN & Heavy Absorption`** (dans la catégorie **`Order Flow - Custom`**) et cliquez sur **Add**.

### Sur macOS (ATAS X)
1. Copiez `FootprintHvnLvnAbsorption.dll` dans :
   ```bash
   ~/Documents/ATAS/Indicators/
   ```
2. Ajoutez **`Footprint HVN / LVN & Heavy Absorption`** depuis la fenêtre des indicateurs d'ATAS X.

---

## ⚙️ Logique & Paramètres de l'Indicateur

### 1. HVN (*High Volume Nodes*) sur le Footprint
- **Intra-Bougie (`2. HVN sur Footprint`)** :
  - Identifie le **POC** de chaque bougie ainsi que tous les clusters dont le volume atteint au moins `Seuil Volume HVN (% du POC)` (par défaut **70 %**) et `Volume Minimum HVN`.
  - Option **`Exiger un pic local de volume`** : vérifie que le niveau possède un volume supérieur ou égal aux niveaux immédiatement adjacents.
  - Encadre la cellule Footprint en **Or / Orange** avec le badge `POC` ou `HVN` et son volume.
- **Profil Composite Multi-Bougies (`4. HVN / LVN Profil Composite`)** :
  - Agrège les volumes Bid + Ask par niveau de prix sur `N` bougies glissantes (par défaut **40 bougies**) ou sur la **Session courante**.
  - Projette des bandes horizontales Support/Résistance sur les pics HVN majeurs (`VPOC / HVN Zone`).

### 2. LVN (*Low Volume Nodes*) sur le Footprint
- **Intra-Bougie (`3. LVN sur Footprint`)** :
  - Détecte les poches de faible liquidité (*creux locaux de volume*) situées **entre** des clusters plus volumineux à l'intérieur de la bougie (`Volume <= 25 % du POC`), en excluant par défaut le dernier tick aux extrémités High/Low (`Ignorer N niveaux aux extrêmes = 1`).
  - Encadre la cellule Footprint en pointillés **Violet / Pourpre** avec le badge `LVN`.
- **Profil Composite Multi-Bougies** :
  - Projette des lignes/zones en pointillés (`LVN Pocket`) sur les vallées de faible volume du profil composite où le prix a tendance à traverser rapidement ou à rejeter vivement.

### 3. Heavy Absorption (*Absorption Massive Bid / Ask*)
Un cluster du Footprint est qualifié de **Heavy Absorption** lorsqu'il franchit simultanément les filtres suivants :
1. **Surcharge de Volume** : Volume du niveau $\ge$ `Volume Minimum` **ET** $\ge$ `Multiplicateur vs Moyenne Bougie` (par défaut **1,6x** le volume moyen des niveaux de la bougie, ou **2,2x+** pour un mur d'absorption intra-bar).
2. **Asymétrie Agressive (Dominance & Imbalance)** :
   - **Bullish Heavy Absorption (au Bid)** : Les vendeurs agressifs frappent massivement le Bid (`Bid / Total >= 62 %` + ratio d'imbalance diagonale `Bid[i] / Ask[i+1] >= 1.8x`), mais les acheteurs limites passifs absorbent tout le flux.
   - **Bearish Heavy Absorption (à l'Ask)** : Les acheteurs agressifs achètent massivement l'Ask (`Ask / Total >= 62 %` + ratio d'imbalance diagonale `Ask[i] / Bid[i-1] >= 1.8x`), mais les vendeurs limites passifs bloquent la hausse.
3. **Localisation & Rejet du Prix (*Trapped Traders*)** :
   - Situé dans la zone d'extrême basse/haute de la bougie (`35 %` du range ou `3 niveaux` de l'extrême, ou mur massif intra-bar) avec clôture de rejet du côté opposé.
4. **Affichage sur le Footprint** :
   - Cellule illuminée en **Cyan Néon** (Bullish Absorption) ou **Magenta Néon** (Bearish Absorption) avec barre latérale d'intensité et badge `ABS▲ Bid x Ask (%)` / `ABS▼ Bid x Ask (%)`.
   - Marqueur au-dessus/sous la bougie (**Losange**, **Flèche**, **Point** ou **Crochet `[ ]`**).
   - **Rayon S/R d'Absorption** projeté vers la droite et arrêté automatiquement dès qu'une bougie ultérieure casse le niveau en clôture (`Arrêter le rayon dès que le niveau est cassé = true`).

---

## 🛠️ Recompiler depuis le code source (Optionnel)

Les bibliothèques de référence d'ATAS X (`.NET 10.0`) sont incluses dans le dossier [`libs/`](./libs/). Pour recompiler le projet vous-même avec le SDK .NET 10 :

```bash
dotnet build -c Release
```
Le fichier `FootprintHvnLvnAbsorption.dll` est généré dans `bin/Release/net10.0/FootprintHvnLvnAbsorption.dll`.
