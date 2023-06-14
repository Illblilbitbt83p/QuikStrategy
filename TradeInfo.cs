using System;

namespace PrismaBoy
{
    [Serializable]

    public class TradeInfo
    {
        public decimal Price;
        public DateTime Time;

        /// <summary>
        /// Конструктор класса TradeInfo
        /// </summary>
        public TradeInfo(decimal price, DateTime time)
        {
            Price = price;
            Time = time;
        }
    }
}
