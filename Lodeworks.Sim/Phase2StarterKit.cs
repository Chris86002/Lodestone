using System;
using System.Collections.Generic;
using System.Linq;

namespace Lodeworks.Sim
{
    public enum StarterPieceKind
    {
        OreDrill, FuelFeedPump, Refinery, TransportTerminal, OreBin,
        FuelFeedBin, MetalBin, LiquidFuelTank, OxidizerTank
    }
    public enum StarterPieceLocation { Packed, Placed, InstalledInHab }

    public sealed class StarterToken
    {
        public StarterToken(string tokenId, StarterPieceKind kind, StarterPieceLocation location,
            int tier, double condition)
        {
            TokenId = Phase2Numbers.Id(tokenId, nameof(tokenId));
            if (!Enum.IsDefined(typeof(StarterPieceKind), kind) ||
                !Enum.IsDefined(typeof(StarterPieceLocation), location) || tier < 0 || tier > 2 ||
                !Phase2Numbers.Finite(condition) || condition < 0 || condition > 1)
                throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind; Location = location; Tier = tier; Condition = condition;
        }
        public string TokenId { get; }
        public StarterPieceKind Kind { get; }
        public StarterPieceLocation Location { get; }
        public int Tier { get; }
        public double Condition { get; }
        public StarterToken With(StarterPieceLocation location, int tier, double condition) =>
            new StarterToken(TokenId, Kind, location, tier, condition);
    }

    public sealed class StarterKitReceipt
    {
        public const int CurrentVersion = 1;
        public StarterKitReceipt(string habId, IEnumerable<StarterToken> tokens)
        {
            HabId = Phase2Numbers.Id(habId, nameof(habId));
            Tokens = Array.AsReadOnly(tokens?.ToArray() ?? throw new ArgumentNullException(nameof(tokens)));
            if (Tokens.Count != 9 || Tokens.Select(x => x.Kind).Distinct().Count() != 9 ||
                Tokens.Select(x => x.TokenId).Distinct(StringComparer.Ordinal).Count() != 9)
                throw new ArgumentException("A starter receipt must contain exactly one of each token.", nameof(tokens));
        }
        public int Version => CurrentVersion;
        public string HabId { get; }
        public IReadOnlyList<StarterToken> Tokens { get; }
    }

    public sealed class StarterKitBook
    {
        private readonly Dictionary<string, StarterKitReceipt> receipts =
            new Dictionary<string, StarterKitReceipt>(StringComparer.Ordinal);
        public IReadOnlyList<StarterKitReceipt> Receipts => Array.AsReadOnly(
            receipts.Values.OrderBy(x => x.HabId, StringComparer.Ordinal).ToArray());
        public bool GrantOnce(string habId, bool integratedTerminal)
        {
            Phase2Numbers.Id(habId, nameof(habId));
            if (receipts.ContainsKey(habId)) return false;
            var tokens = Enum.GetValues(typeof(StarterPieceKind)).Cast<StarterPieceKind>()
                .Select(kind => new StarterToken(habId + "/" + kind, kind,
                    kind == StarterPieceKind.TransportTerminal && integratedTerminal ?
                        StarterPieceLocation.InstalledInHab : StarterPieceLocation.Packed, 0, 1));
            receipts.Add(habId, new StarterKitReceipt(habId, tokens));
            return true;
        }
        public StarterToken? Find(string habId, StarterPieceKind kind) =>
            receipts.TryGetValue(habId, out StarterKitReceipt receipt) ?
                receipt.Tokens.FirstOrDefault(x => x.Kind == kind) : null;
        public bool TryMove(string habId, StarterPieceKind kind, StarterPieceLocation expected,
            StarterPieceLocation next)
        {
            Phase2Numbers.Id(habId, nameof(habId));
            if (!receipts.TryGetValue(habId, out StarterKitReceipt receipt)) return false;
            StarterToken? token = receipt.Tokens.FirstOrDefault(x => x.Kind == kind);
            bool validMove = expected == StarterPieceLocation.Packed && next == StarterPieceLocation.Placed ||
                expected == StarterPieceLocation.Placed && next == StarterPieceLocation.Packed;
            if (token == null || token.Location != expected || !validMove)
                return false;
            StarterToken replacement = token.With(next, token.Tier, token.Condition);
            receipts[habId] = new StarterKitReceipt(habId,
                receipt.Tokens.Select(x => x.Kind == kind ? replacement : x));
            return true;
        }
        public bool TryRecordWear(string habId, StarterPieceKind kind, double condition)
        {
            Phase2Numbers.Nonnegative(condition, nameof(condition));
            if (!receipts.TryGetValue(Phase2Numbers.Id(habId, nameof(habId)), out StarterKitReceipt receipt)) return false;
            StarterToken? token = receipt.Tokens.FirstOrDefault(x => x.Kind == kind);
            if (token == null || condition > token.Condition || condition > 1) return false;
            StarterToken replacement = token.With(token.Location, token.Tier, condition);
            receipts[habId] = new StarterKitReceipt(habId,
                receipt.Tokens.Select(x => x.Kind == kind ? replacement : x));
            return true;
        }
        public static StarterKitBook Restore(IEnumerable<StarterKitReceipt> snapshots)
        {
            if (snapshots == null) throw new ArgumentNullException(nameof(snapshots));
            var result = new StarterKitBook();
            foreach (StarterKitReceipt receipt in snapshots)
            {
                if (receipt == null || receipt.Version != StarterKitReceipt.CurrentVersion ||
                    result.receipts.ContainsKey(receipt.HabId))
                    throw new ArgumentException("Invalid or duplicate starter receipt.", nameof(snapshots));
                result.receipts.Add(receipt.HabId, receipt);
            }
            return result;
        }
    }
}

