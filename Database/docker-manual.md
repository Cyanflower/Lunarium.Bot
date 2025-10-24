根据你的情况和之前的讨论，我推荐以下**完整方案**：

## **推荐方案：Docker + 绑定挂载 + pg_dump 定时备份**

### **1. 修改 docker-compose.yml**

```yaml
services:
  LunariumBot.NET.DB:
    image: postgres:17-bookworm
    container_name: LunariumBot.NET.DB
    shm_size: 512mb
    restart: always
    environment:
      POSTGRES_USER: 'LunariumBot.NET'
      POSTGRES_PASSWORD: '***...'
      POSTGRES_DB: 'LunariumBot.NET.DB'
      TZ: "Asia/Shanghai"
      PGTZ: "Asia/Shanghai"
    ports:
      - "0.0.0.0:52001:5432"
    volumes:
      # 数据目录 - 绑定到宿主机
      - /opt/docker-data/postgres:/var/lib/postgresql/data
      # 备份目录 - 方便从容器内备份
      - /opt/backups/postgres:/backups
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U LunariumBot.NET -d LunariumBot.NET.DB"]
      interval: 10s
      timeout: 5s
      retries: 5
```

### **2. 准备目录**

```bash
# 创建数据和备份目录
sudo mkdir -p /opt/docker-data/postgres
sudo mkdir -p /opt/backups/postgres
sudo mkdir -p /opt/scripts

# 设置 PostgreSQL 目录权限（UID 999 是容器内的 postgres 用户）
sudo chown -R 999:999 /opt/docker-data/postgres
sudo chmod 700 /opt/docker-data/postgres

# 备份目录权限
sudo chmod 755 /opt/backups/postgres
```

### **3. 创建备份脚本**

创建 `/opt/scripts/backup-postgres.sh`：

```bash
#!/bin/bash
set -e

# ===== 配置区 =====
CONTAINER_NAME="LunariumBot.NET.DB"
DB_USER="LunariumBot.NET"
DB_NAME="LunariumBot.NET.DB"
BACKUP_DIR="/opt/backups/postgres"
RETENTION_DAYS=7
LOG_FILE="/var/log/postgres-backup.log"

# ===== 执行备份 =====
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="$BACKUP_DIR/backup_${TIMESTAMP}.sql.gz"

echo "[$(date '+%Y-%m-%d %H:%M:%S')] 开始备份..." | tee -a "$LOG_FILE"

# 运行时备份（无需停机）
if docker exec "$CONTAINER_NAME" pg_dump -U "$DB_USER" "$DB_NAME" | gzip > "$BACKUP_FILE"; then
    FILE_SIZE=$(du -h "$BACKUP_FILE" | cut -f1)
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] ✓ 备份成功: $BACKUP_FILE (大小: $FILE_SIZE)" | tee -a "$LOG_FILE"
    
    # 验证备份完整性
    if gunzip -t "$BACKUP_FILE" 2>/dev/null; then
        echo "[$(date '+%Y-%m-%d %H:%M:%S')] ✓ 备份文件完整性验证通过" | tee -a "$LOG_FILE"
    else
        echo "[$(date '+%Y-%m-%d %H:%M:%S')] ✗ 警告：备份文件可能已损坏" | tee -a "$LOG_FILE"
    fi
else
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] ✗ 备份失败！" | tee -a "$LOG_FILE"
    exit 1
fi

# ===== 清理旧备份 =====
OLD_BACKUPS=$(find "$BACKUP_DIR" -name "backup_*.sql.gz" -mtime +$RETENTION_DAYS)
if [ -n "$OLD_BACKUPS" ]; then
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] 清理 ${RETENTION_DAYS} 天前的备份..." | tee -a "$LOG_FILE"
    find "$BACKUP_DIR" -name "backup_*.sql.gz" -mtime +$RETENTION_DAYS -delete
fi

# ===== 显示备份列表 =====
BACKUP_COUNT=$(ls -1 "$BACKUP_DIR"/backup_*.sql.gz 2>/dev/null | wc -l)
echo "[$(date '+%Y-%m-%d %H:%M:%S')] 当前保留 $BACKUP_COUNT 个备份文件" | tee -a "$LOG_FILE"
echo "----------------------------------------" | tee -a "$LOG_FILE"
```

**设置权限：**
```bash
sudo chmod +x /opt/scripts/backup-postgres.sh
```

### **4. 启动 PostgreSQL 容器**

```bash
# 进入 docker-compose.yml 所在目录
cd /path/to/your/compose/file

# 启动容器
sudo docker compose up -d

# 查看状态
sudo docker compose ps
sudo docker compose logs -f LunariumBot.NET.DB
```

### **5. 测试备份**

```bash
# 手动执行一次备份测试
sudo /opt/scripts/backup-postgres.sh

# 检查备份文件
ls -lh /opt/backups/postgres/

# 查看日志
cat /var/log/postgres-backup.log
```

### **6. 设置定时备份（Cron）**

```bash
# 编辑 root 用户的 crontab
sudo crontab -e

# 添加以下内容（根据需求选择一个）：

# 方案A: 每天凌晨 3 点备份
0 3 * * * /opt/scripts/backup-postgres.sh >> /var/log/postgres-backup.log 2>&1

# 方案B: 每 6 小时备份一次
0 */6 * * * /opt/scripts/backup-postgres.sh >> /var/log/postgres-backup.log 2>&1

# 方案C: 每天 3 次（凌晨3点、中午12点、晚上9点）
0 3,12,21 * * * /opt/scripts/backup-postgres.sh >> /var/log/postgres-backup.log 2>&1
```

### **7. 恢复备份（当需要时）**

```bash
# 从备份恢复（会覆盖现有数据，谨慎操作！）
gunzip < /opt/backups/postgres/backup_20251024_030000.sql.gz | \
  sudo docker exec -i LunariumBot.NET.DB psql -U LunariumBot.NET LunariumBot.NET.DB

# 或者先解压再恢复
gunzip /opt/backups/postgres/backup_20251024_030000.sql.gz
sudo docker exec -i LunariumBot.NET.DB psql -U LunariumBot.NET LunariumBot.NET.DB < backup_20251024_030000.sql
```

## **方案优势总结**

✅ **运行时备份**：使用 pg_dump，无需停机，完全安全  
✅ **数据持久化**：绑定挂载到 `/opt/docker-data/postgres`，直接访问  
✅ **自动化**：Cron 定时执行，无需人工干预  
✅ **空间管理**：自动清理 7 天前的旧备份  
✅ **完整性验证**：每次备份后自动验证文件完整性  
✅ **日志记录**：所有操作记录到 `/var/log/postgres-backup.log`  