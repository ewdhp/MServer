#!/bin/sh

# Create the directory for node scripts
mkdir -p /home/ewd/MServer/node-scripts

# Create sample node1.sh
cat <<'EOF' > /home/ewd/MServer/node-scripts/node1.sh
#!/bin/sh
echo "node1.sh received: $@"
EOF

# Create sample node2.sh
cat <<'EOF' > /home/ewd/MServer/node-scripts/node2.sh
#!/bin/sh
echo "node2.sh received: $@"
EOF

# Create sample node3.sh
cat <<'EOF' > /home/ewd/MServer/node-scripts/node3.sh
#!/bin/sh
echo "node3.sh received: $@"
EOF

# Create sample node4.sh
cat <<'EOF' > /home/ewd/MServer/node-scripts/node4.sh
#!/bin/sh
echo "node4.sh received: $@"
EOF

# Make all scripts executable
chmod +x /home/ewd/MServer/node-scripts/node*.sh
