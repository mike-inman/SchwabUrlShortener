if .name != $revision then
  error("revision name mismatch")
elif (.properties | type) != "object" then
  error("revision properties missing")
else
  [(.properties.runningState // "pending"), (.properties.healthState // "pending")] | @tsv
end
