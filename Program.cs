/*
ŠALABAHTER VARIJABLI / OZNAKA (kratko objašnjenje)

Ulazi (postavljaš ih ručno u ovom file-u; KOD JE FLEKSIBILAN – radi za bilo koji broj kredita):
- max_annuity           : mjesečni limit opterećenja klijenta (EUR/mj)
- interest_rate         : godišnja kamatna stopa (npr. 0.07 za 7% godišnje)
- tenor                 : broj mjesečnih anuiteta novog kredita (npr. 59)
- Loan_amount           : željena gotovina (EUR)
- Max_Addiko_Exposure   : maksimalna ukupna izloženost klijenta u NAŠOJ banci nakon isplate (EUR)
- deltaW                : korak diskretizacije “vrijednosti” (npr. 50 EUR) – manji = preciznije (sporije), veći = brže
- deltaE                : korak diskretizacije izloženosti (npr. 50 EUR)

Podaci o postojećim kreditima (n komada; unosiš direktno niže u polja – primjer s 6 kredita):
- refin_amount[i]       : preostala glavnica za zatvaranje kredita i (EUR)
- annuity[i]            : iznos mjesečnog anuiteta kredita i (EUR)
- IsAddiko[i]           : 1 ako je kredit i u NAŠOJ banci (zatvaranje ne troši plafon izloženosti), 0 ako je vanjski

Izračunava se interno:
- r_month               : mjesečna kamatna stopa novog kredita (= interest_rate / 12)
- k                     : anuitetni faktor = (1 - (1+r_month)^(-tenor)) / r_month
- Ptot                  : zbroj postojećih mjesečnih anuiteta = SUM(annuity[i])
- base                  : glavnica koja stane bez zatvaranja = k * (max_annuity - Ptot)
- w[i]                  : neto-korist zatvaranja i = k * annuity[i] - refin_amount[i]
- inBankExposureNow     : zbroj refin_amount za IsAddiko=1 (naši krediti)
- E_base                : preostali plafon izloženosti prije gotovine = Max_Addiko_Exposure - inBankExposureNow
- E0                    : plafon koji smiješ potrošiti na zatvaranje VANJSKIH = E_base - Loan_amount
- T                     : cilj “vrijednosti” = Loan_amount - base

Ciljevi:
- Primarno: ako je moguće, naći skup kredita za zatvaranje s minimalnim ukupnim iznosom zatvaranja (SUM refin_amount) takav da klijent dobije traženu gotovinu Loan_amount.
- Ako nije moguće: izračunati maksimalnu moguću gotovinu X_max i pripadni skup zatvaranja.

IZLAZ:
- Ispisuje se niz 0/1 duljine n: 1 znači da se kredit zatvara, 0 da se ne zatvara.
  Ako je Loan_amount izvediv: niz predstavlja minimalni skup po ukupnom iznosu zatvaranja.
  Ako nije izvediv: niz predstavlja skup koji postiže maksimalnu moguću gotovinu (X_max).
*/

using System;
using System.Linq;
using System.Collections.Generic;

public class Program
{
    // ----------------------------
    // KONFIGURACIJA / ULAZI
    // ----------------------------
    static double max_annuity = 900.0;
    static double interest_rate = 0.07;   // 7% godišnje
    static int tenor = 59;     // broj mjeseci novog kredita
    static double Loan_amount = 10000;  // željena gotovina
    static double Max_Addiko_Exposure = 40000;  // plafon izloženosti u NAŠOJ banci (mijenjaj po potrebi)

    // Diskretizacija (preporuka: 50)
    static int deltaW = 50;   // vrijednost (w) u koraku 50 EUR
    static int deltaE = 50;   // izloženost (R) u koraku 50 EUR

    // ----------------------------
    // POPIS KREDITA (fleksibilno: možeš promijeniti broj redaka; primjer s 6)
    // ----------------------------
    static double[] refin_amount = new double[] { 3500, 6700, 9800, 4200, 2500, 3100 };
    static double[] annuity = new double[] { 350, 335, 196, 180, 140, 120 };
    static int[] IsAddiko = new int[] { 0, 0, 1, 0, 1, 0 }; // 1=naš, 0=vanjski

    public static void Main()
    {
        int n = refin_amount.Length;
        if (annuity.Length != n  || IsAddiko.Length != n)
        {
            Console.WriteLine("Greška: nizovi kredita nisu jednake duljine.");
            return;
        }

        // 0) Priprema
        double r_month = interest_rate / 12.0;
        double k = (1.0 - Math.Pow(1.0 + r_month, -tenor)) / r_month;
        double Ptot = annuity.Sum();
        double baseCap = k * (max_annuity - Ptot);

        // w[i] = k * annuity[i] - refin_amount[i]
        double[] w = new double[n];
        for (int i = 0; i < n; i++)
            w[i] = k * annuity[i] - refin_amount[i];

        double inBankExposureNow = 0.0;
        for (int i = 0; i < n; i++)
            if (IsAddiko[i] == 1) inBankExposureNow += refin_amount[i];

        double E_base = Max_Addiko_Exposure - inBankExposureNow; // plafon prije gotovine

        // Nužne brze provjere
        double sumPosW = w.Where(x => x > 0).Sum();
        if (baseCap + sumPosW < Loan_amount || E_base < Loan_amount)
        {
            // Nije izvedivo za traženi Loan_amount -> izračunaj maksimum X_max
            var selForMax = MaxCashSet(baseCap, w, refin_amount, IsAddiko, E_base, deltaE);
            PrintSelection(selForMax.selection, "NOT_FEASIBLE_FOR_C (max cash set)");
            return;
        }

        // A) Pokušaj egzaktno: minimalni ukupni iznos zatvaranja za traženi Loan_amount
        var res = MinRefinSumForLoan(Loan_amount, baseCap, w, refin_amount, IsAddiko, E_base, deltaW, deltaE);

        if (res.feasible)
        {
            PrintSelection(res.selection, "FEASIBLE_FOR_C (min total refin set)");
        }
        else
        {
            var selForMax = MaxCashSet(baseCap, w, refin_amount, IsAddiko, E_base, deltaE);
            PrintSelection(selForMax.selection, "NOT_FEASIBLE_FOR_C (max cash set)");
        }
    }

    // Ispis niza 0/1
    static void PrintSelection(int[] sel, string label)
    {
        Console.WriteLine(label);
        Console.WriteLine(string.Join(" ", sel.Select(x => x.ToString())));
    }

    // Egzaktno: minimalni SUM refin_amount za zadani Loan_amount (2D DP po vrijednosti i vanjskoj izloženosti)
    static (bool feasible, int[] selection) MinRefinSumForLoan(
        double Loan_amount, double baseCap,
        double[] w, double[] R, int[] IsAddiko,
        double E_base, int deltaW, int deltaE)
    {
        int n = w.Length;
        double T = Loan_amount - baseCap;
        if (T <= 0)
        {
            // Ništa ne treba zatvoriti
            return (true, new int[n]); // sve nule
        }

        double E0 = E_base - Loan_amount;
        if (E0 < 0) return (false, new int[n]);

        // Diskretizacija
        double sumPosW = w.Where(x => x > 0).Sum();
        int VmaxIdx = Math.Max(0, (int)Math.Ceiling(sumPosW / deltaW));
        int E0Idx = Math.Max(0, (int)Math.Ceiling(E0 / deltaE));
        int TIdx = Math.Max(0, (int)Math.Ceiling(T / deltaW));

        double INF = 1e300;
        double[,] DP = new double[E0Idx + 1, VmaxIdx + 1];
        (int pe, int pv, int i)[,] Parent = new (int, int, int)[E0Idx + 1, VmaxIdx + 1];

        for (int e = 0; e <= E0Idx; e++)
        {
            for (int v = 0; v <= VmaxIdx; v++)
            {
                DP[e, v] = INF;
                Parent[e, v] = (-1, -1, -1);
            }
        }
        DP[0, 0] = 0.0;

        for (int idx = 0; idx < n; idx++)
        {
            if (w[idx] <= 0) continue;

            int vGain = Math.Max(1, (int)Math.Ceiling(w[idx] / deltaW));
            int eGain = (IsAddiko[idx] == 1) ? 0 : Math.Max(0, (int)Math.Ceiling(R[idx] / deltaE));
            double costR = R[idx];

            for (int e = E0Idx; e >= 0; e--)
            {
                int newE = e + eGain;
                if (newE > E0Idx) continue;

                for (int v = VmaxIdx; v >= 0; v--)
                {
                    int newV = v + vGain;
                    if (newV > VmaxIdx) newV = VmaxIdx;

                    double newCost = DP[e, v] + costR;
                    if (newCost < DP[newE, newV])
                    {
                        DP[newE, newV] = newCost;
                        Parent[newE, newV] = (e, v, idx);
                    }
                }
            }
        }

        // Odaberi najbolje stanje s v >= TIdx, e bilo koji
        double bestCost = INF;
        int be = -1, bv = -1;
        for (int e = 0; e <= E0Idx; e++)
        {
            for (int v = TIdx; v <= VmaxIdx; v++)
            {
                if (DP[e, v] < bestCost)
                {
                    bestCost = DP[e, v];
                    be = e; bv = v;
                }
            }
        }

        if (bestCost >= INF / 2)
        {
            return (false, new int[n]);
        }

        // Rekonstrukcija skupa
        int[] take = new int[n];
        int ce = be, cv = bv;
        while (ce >= 0 && cv >= 0)
        {
            var par = Parent[ce, cv];
            if (par.i == -1) break;
            take[par.i] = 1;
            ce = par.pe;
            cv = par.pv;
        }

        return (true, take);
    }

    // Egzaktno: maksimalna moguća gotovina X_max i pripadni skup (1D knapsack po vanjskoj izloženosti)
    static (double Xmax, int[] selection) MaxCashSet(
        double baseCap, double[] w, double[] R, int[] IsAddiko,
        double E_base, int deltaE)
    {
        int n = w.Length;

        // W_B = suma pozitivnih w za "naše" (ne troše plafon)
        double WB = 0.0;
        for (int i = 0; i < n; i++)
            if (IsAddiko[i] == 1 && w[i] > 0) WB += w[i];

        // Vanjski kandidati s w>0
        List<int> X = new List<int>();
        for (int i = 0; i < n; i++)
            if (IsAddiko[i] == 0 && w[i] > 0) X.Add(i);

        int EcapIdx = Math.Max(0, (int)Math.Ceiling(E_base / deltaE));

        // 1D knapsack: W_e[e] = max w (vanjskih) uz potrošnju e*deltaE
        double[] W_e = new double[EcapIdx + 1];
        (int prevE, int idx)[] Prev = new (int, int)[EcapIdx + 1];
        for (int e = 0; e <= EcapIdx; e++) { W_e[e] = 0.0; Prev[e] = (-1, -1); }

        foreach (var i in X)
        {
            int eCost = Math.Max(0, (int)Math.Ceiling(R[i] / deltaE));
            double vGain = w[i];
            for (int e = EcapIdx; e >= eCost; e--)
            {
                double cand = W_e[e - eCost] + vGain;
                if (cand > W_e[e])
                {
                    W_e[e] = cand;
                    Prev[e] = (e - eCost, i);
                }
            }
        }

        // X(e) = min( base + WB + W_e[e],  E_base - e*deltaE )
        double Xmax = 0.0;
        int argE = 0;
        for (int e = 0; e <= EcapIdx; e++)
        {
            double cash1 = baseCap + WB + W_e[e];
            double cash2 = E_base - e * (double)deltaE;
            double Xe = Math.Min(cash1, cash2);
            if (Xe > Xmax)
            {
                Xmax = Xe;
                argE = e;
            }
        }

        // Rekonstrukcija vanjskog dijela skupa
        int[] take = new int[n];
        int cur = argE;
        while (cur >= 0 && Prev[cur].idx != -1)
        {
            int idx = Prev[cur].idx;
            take[idx] = 1;
            cur = Prev[cur].prevE;
        }

        // "Naše" s w>0 po želji možeš označiti 1 (ne troše plafon); po defaultu ostaju 0.
        // Ako želiš ih uvijek zatvarati kad pomažu, odkomentiraj:
        // for (int i = 0; i < n; i++) if (IsAddiko[i] == 1 && w[i] > 0) take[i] = 1;

        return (Xmax, take);
    }
}
