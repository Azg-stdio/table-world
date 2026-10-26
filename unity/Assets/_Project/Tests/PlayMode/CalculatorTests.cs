using NUnit.Framework;
using TableWorld.View.Hud;
using UnityEngine.UIElements;

namespace TableWorld.Tests.PlayMode
{
    /// <summary>The pocket calculator adds up like a basic calculator: each operator applies to the running total.</summary>
    public class CalculatorTests
    {
        private static string Keys(CalculatorPanel calculator, string keys)
        {
            foreach (var key in keys)
                calculator.Press(key);
            return calculator.Display;
        }

        [Test]
        public void SumsChainAndClear()
        {
            var calculator = new CalculatorPanel(new VisualElement(), null);
            Assert.AreEqual("42", Keys(calculator, "12+30="));
            Assert.AreEqual("84", Keys(calculator, "×2="), "an operator after = goes on from the result");
            Assert.AreEqual("9", Keys(calculator, "4+5="), "digits after = start a new sum");
            Assert.AreEqual("17", Keys(calculator, "3+4+10="));
            Assert.AreEqual("3.5", Keys(calculator, "C7÷2="));
            Assert.AreEqual("-6", Keys(calculator, "C4−10="));
            Assert.AreEqual("0", Keys(calculator, "C"));
        }
    }
}
