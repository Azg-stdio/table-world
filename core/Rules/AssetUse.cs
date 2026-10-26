namespace Core.Rules
{
    /// <summary>Which assets an object draws with, so a view redraws only the pieces a newly arrived file concerns.</summary>
    public static class AssetUse
    {
        /// <summary>True when <paramref name="obj"/> (or a card in it, for a deck) names <paramref name="assetId"/>.</summary>
        public static bool Uses(TableObject obj, string assetId)
        {
            switch (obj)
            {
                case Card card:
                    return card.FaceAsset == assetId || card.BackAsset == assetId || card.Text?.Art == assetId ||
                           TextFaces.IsTextId(assetId) && (TextFaces.FaceId(card) == assetId || TextFaces.BackId(card) == assetId);
                case Deck deck:
                    if (deck.FaceAsset == assetId || deck.BackAsset == assetId)
                        return true;
                    var text = TextFaces.IsTextId(assetId);
                    foreach (var entry in deck.Cards)
                        if (entry.FaceAsset == assetId || entry.BackAsset == assetId || entry.Text?.Art == assetId ||
                            text && (TextFaces.FaceId(deck, entry) == assetId || TextFaces.BackId(deck, entry) == assetId))
                            return true;
                    return false;
                case Token token:
                    return token.Asset == assetId;
                case Tile tile:
                    return tile.Asset == assetId;
                case Die die:
                    return die.Asset == assetId;
                case Model model:
                    return model.Asset == assetId || model.DiffuseAsset == assetId;
                case Bag bag:
                    return bag.Asset == assetId || bag.DiffuseAsset == assetId;
                case Board board:
                    return board.Asset == assetId;
                case Figure figure:
                    return figure.Asset == assetId;
                case Pdf pdf:
                    return pdf.Asset == assetId;
                default:
                    return false;
            }
        }
    }
}
