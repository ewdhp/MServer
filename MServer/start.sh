# Identify and kill the process using port 5000
PID_5000=$(sudo lsof -t -i :5000)
if [ -n "$PID_5000" ]; then
  echo "Killing process $PID_5000 using port 5000"
  sudo kill -9 $PID_5000
else
  echo "No process found using port 5000"
fi

# Identify and kill the process using port 5001
PID_5001=$(sudo lsof -t -i :5001)
if [ -n "$PID_5001" ]; then
  echo "Killing process $PID_5001 using port 5001"
  sudo kill -9 $PID_5001
else
  echo "No process found using port 5001"
fi


# Run the application
dotnet build && dotnet run

#curl -k -X POST "https://localhost:5001/api/auth/send" -H "Content-Type: application/json" -d '{"phone": "+523321890176"}'
#curl -k -X POST "https://localhost:5001/api/auth/verify"  -H "Content-Type: application/json" -d '{"phone": "+523321890176", "code": "133589"}'

