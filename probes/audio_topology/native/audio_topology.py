"""Optional KS audio connector data. Unsupported interfaces remain explicit errors."""
import sys

def collect_jacks(endpoint_id: str):
    if sys.platform != "win32":
        return [], [{"scope": endpoint_id, "property": "IKsJackDescription", "error": "僅支援 Windows"}]
    # pycaw does not expose the KS connector interfaces. Keep this as a recorded
    # capability gap rather than guessing a connector from endpoint text.
    return [], [{"scope": endpoint_id, "property": "IKsJackDescription/IKsJackDescription2", "error": "此探針版本尚未實作 KS connector COM 查詢"},
                {"scope": endpoint_id, "property": "IKsJackSinkInformation", "error": "此探針版本尚未實作 KS sink COM 查詢"}]

def collect_jack_sink(endpoint_id: str):
    records, errors = collect_jacks(endpoint_id)
    return None, errors
