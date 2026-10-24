namespace Core.Rules
{
    /// <summary>
    /// How much of the table an object covers (width along its X, depth along its Z, meters), for laying objects out
    /// (G18). The defaults match the sizes the Unity view draws (ObjectViewFactory); a PlayMode test keeps them in step.
    /// </summary>
    public static class Footprint
    {
        public const float Token = 0.025f;
        public const float Tile = 0.05f;
        public const float Die = 0.02f;
        public const float Bag = 0.08f;
        public const float Figure = 0.03f;
        public const float Model = 0.03f;
        public static readonly Vec2 Board = new Vec2(0.5f, 0.5f);
        public static readonly Vec2 Counter = new Vec2(0.07f, 0.035f);
        public static readonly Vec2 Clock = new Vec2(0.09f, 0.04f);

        public static Vec2 Of(TableObject obj)
        {
            var size = obj switch
            {
                Card card => card.Size ?? Card.DefaultSize,
                Deck deck => deck.Size ?? Card.DefaultSize,
                Token _ => new Vec2(Token, Token),
                Tile _ => new Vec2(Tile, Tile),
                Rules.Die _ => new Vec2(Die, Die),
                Rules.Bag _ => new Vec2(Bag, Bag),
                Rules.Board board => board.Size ?? Board,
                Rules.Figure _ => new Vec2(Figure, Figure),
                Rules.Counter _ => Counter,
                Rules.Clock _ => Clock,
                _ => new Vec2(Model, Model),
            };
            var scale = obj.Transform.Scale;
            return new Vec2(size.X * scale.X, size.Y * scale.Z);
        }
    }
}
