using LogicModule.Nodes.Helpers;
using LogicModule.ObjectModel;
using LogicModule.ObjectModel.TypeSystem;
using System;
using System.Globalization;

namespace teclab_at.logic
{
    public class TempController : LogicNodeBase
    {
        private readonly IPersistenceService persistenceService;
        public TempController(INodeContext context)
        : base(context)
        {
            // check
            context.ThrowIfNull("context");

            // init the persistence service storing value to flash
            this.persistenceService = context.GetService<IPersistenceService>();

            // Initialize the input ports
            var typeService = context.GetService<ITypeService>();
            this.SetTemp = typeService.CreateDouble(PortTypes.Number, "SetTemp");
            this.CurTemp = typeService.CreateDouble(PortTypes.Number, "CurTemp");
            this.Cooling = typeService.CreateBool(PortTypes.Binary, "Cooling");
            this.Heating = typeService.CreateBool(PortTypes.Binary, "Heating");
            this.Manual = typeService.CreateBool(PortTypes.Binary, "Manual");
            // Initialize the output ports
            this.Valve = typeService.CreateBool(PortTypes.Binary, "Valve");
            this.StoTemp = typeService.CreateDouble(PortTypes.Number, "StoTemp");
        }

        [Input(DisplayOrder = 1, IsRequired = true)]
        public DoubleValueObject SetTemp { get; private set; }
        
        [Input(DisplayOrder = 2, IsRequired = true)] 
        public DoubleValueObject CurTemp { get; private set; }
        
        [Input(DisplayOrder = 3, IsRequired = true)]
        public BoolValueObject Cooling { get; private set; }

        [Input(DisplayOrder = 4, IsRequired = true)] 
        public BoolValueObject Heating { get; private set; }

        [Input(DisplayOrder = 5, IsRequired = true)]
        public BoolValueObject Manual { get; private set; }

        [Output(DisplayOrder = 1)]
        public BoolValueObject Valve { get; private set; }
        
        [Output(DisplayOrder = 2)]
        public DoubleValueObject StoTemp { get; private set; }

        /// <summary>
        /// This function is called every time any input (marked by attribute [Input]) receives a value and no input has no value.
        /// The inputs that were updated for this function to be called, have <see cref="IValueObject.WasSet"/> set to true. After this function returns 
        /// the <see cref="IValueObject.WasSet"/> flag will be reset to false.
        /// </summary>
        public override void Execute()
        {
            // only update the current set temperatur if the value changed by 0.1 degree
            // that is to avoid infinite loops
            if (!this.StoTemp.HasValue || (this.SetTemp.WasSet && (Math.Abs(this.SetTemp.Value - this.StoTemp.Value) >= 0.1)))
            {
                this.StoTemp.Value = this.SetTemp.Value;
                this.persistenceService.SetValue(this, "StoTemp", this.StoTemp.Value.ToString());
            }

            // now control the valve
            if (this.Manual.Value)
            {
                this.Valve.Value = true;
            }
            else if (this.Cooling.Value && this.Heating.Value)
            {
                this.Valve.Value = false;
            }
            else if (this.Cooling.Value)
            {
                if (this.CurTemp.Value > this.SetTemp.Value)
                {
                    this.Valve.Value = true;
                }
                else
                {
                    this.Valve.Value = false;
                }
            }
            else if (this.Heating.Value)
            {
                if (this.CurTemp.Value < this.SetTemp.Value)
                {
                    this.Valve.Value = true;
                }
                else
                {
                    this.Valve.Value = false;
                }
            }
            else
            {
                this.Valve.Value = false;
            }
        }

        /// <summary>
        /// This function is called only once when the logic page is being loaded.
        /// The base function of this is empty. 
        /// </summary>
        public override void Startup()
        {
            // call base
            base.Startup();

            // read stored/persistent temperature setting
            string savedValue = this.persistenceService.GetValue(this, "StoTemp");
            if (string.IsNullOrEmpty(savedValue)) return;
            
            // try to convert to double
            try
            {
                this.StoTemp.Value = Convert.ToDouble(savedValue, CultureInfo.CurrentCulture);
            }
            catch
            {
                //this.StoTemp.Value = 20;
                return;
            }
            this.SetTemp.Value = this.StoTemp.Value;
        }

        /// <summary>
        /// By default this function gets the translation for the node's in- and output from the <see cref="LogicNodeBase.ResourceManager"/>.
        /// A resource file with translation is required for this to work.
        /// </summary>
        /// <param name="language">The requested language, for example "en" or "de".</param>
        /// <param name="key">The key to translate.</param>
        /// <returns>The translation of <paramref name="key"/> in the requested language, or <paramref name="key"/> if the translation is missing.</returns>
        public override string Localize(string language, string key)
        {
            return base.Localize(language, key);
        }
    }
}
